
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Recruitment.Api.Data;
using Recruitment.Api.Dtos.Auth;
using Recruitment.Api.Entities;
using Microsoft.Data.SqlClient;

namespace Recruitment.Api.Services.Auth;

public class AuthService : IAuthService
{

    private readonly AppDbContext _db;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly JwtTokenService _jwtTokenService;
    private readonly IGoogleTokenValidator? _googleTokenValidator;


    public AuthService(
        AppDbContext db,
        IPasswordHasher<User> passwordHasher,
        JwtTokenService jwtTokenService,
        IGoogleTokenValidator? googleTokenValidator = null)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _googleTokenValidator = googleTokenValidator;
    }



    public async Task<RegisterResponse> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        var username = request.Username.Trim().ToLowerInvariant();
        var email = request.Email.Trim().ToLowerInvariant();

        if (await _db.Users.AnyAsync(
            u => u.Username == username, cancellationToken))
            throw new RegistrationConflictException("Tên đăng nhập đã tồn tại.");

        if (await _db.Users.AnyAsync(
            u => u.Email == email, cancellationToken))
            throw new RegistrationConflictException("Email đã tồn tại.");

        var user = new User
        {
            UserId = Guid.NewGuid().ToString(),
            Username = username,
            Email = email,
            FullName = request.FullName.Trim(),
            PhoneNumber = request.PhoneNumber.Trim(),
            RoleId = "CANDIDATE",
            AccountStatus = AccountStatus.ACTIVE
        };

        user.PasswordHash = _passwordHasher.HashPassword(
            user, request.Password);

        _db.Users.Add(user);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is SqlException sqlEx
                  && (sqlEx.Number == 2601 || sqlEx.Number == 2627))
        {
            throw new RegistrationConflictException(
                "Email hoặc tên đăng nhập đã tồn tại.");
        }


        return new RegisterResponse
        {
            UserId = user.UserId,
            Username = user.Username,
            Email = user.Email,
            FullName = user.FullName,
            Role = user.RoleId
        };
    }

    public async Task<LoginResponse> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var username = request.Username.Trim().ToLowerInvariant();

        var user = await _db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(
                u => u.Username == username,
                cancellationToken);

        if (user == null || string.IsNullOrEmpty(user.PasswordHash))
            throw new UnauthorizedAccessException(
                "Tên đăng nhập hoặc mật khẩu không chính xác.");

        var result = _passwordHasher.VerifyHashedPassword(
            user, user.PasswordHash, request.Password);

        if (result == PasswordVerificationResult.Failed)
            throw new UnauthorizedAccessException(
                "Tên đăng nhập hoặc mật khẩu không chính xác.");

        if (user.AccountStatus != AccountStatus.ACTIVE ||
            user.DeletedAtUtc != null)
            throw new UnauthorizedAccessException(
                "Tài khoản không thể đăng nhập.");

        var (token, expiresAt) = _jwtTokenService.CreateToken(user);

        return new LoginResponse
        {
            AccessToken = token,
            TokenType = "Bearer",
            ExpiresAtUtc = expiresAt,
            UserId = user.UserId,
            Username = user.Username,
            Role = user.RoleId
        };
    }

    public async Task ChangePasswordAsync(
        string userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await _db.Users
            .SingleOrDefaultAsync(
                u => u.UserId == userId,
                cancellationToken);

        if (user == null ||
            user.AccountStatus != AccountStatus.ACTIVE ||
            user.DeletedAtUtc != null)
        {
            throw new UnauthorizedAccessException(
                "Tài khoản không khả dụng.");
        }

        if (string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            throw new InvalidOperationException(
                "Tài khoản chưa có mật khẩu nội bộ.");
        }

        var currentPasswordResult = _passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            request.CurrentPassword);

        if (currentPasswordResult == PasswordVerificationResult.Failed)
        {
            throw new ArgumentException(
                "Mật khẩu hiện tại không chính xác.");
        }

        var newPasswordResult = _passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            request.NewPassword);

        if (newPasswordResult != PasswordVerificationResult.Failed)
        {
            throw new ArgumentException(
                "Mật khẩu mới phải khác mật khẩu hiện tại.");
        }

        user.PasswordHash = _passwordHasher.HashPassword(
            user,
            request.NewPassword);

        user.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<LoginResponse> GoogleLoginAsync(
        GoogleLoginRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_googleTokenValidator == null)
            throw new InvalidOperationException(
                "Google Token Validator chưa được cấu hình.");

        // 1. Xác minh Google ID Token
        var payload = await _googleTokenValidator.ValidateAsync(
            request.IdToken);

        var googleId = payload.Subject;
        var email = payload.Email.Trim().ToLowerInvariant();

        // 2. Tìm tài khoản đã liên kết Google
        var user = await _db.Users.SingleOrDefaultAsync(
            u => u.GoogleId == googleId,
            cancellationToken);

        if (user == null)
        {
            // Không tự động liên kết chỉ vì trùng email
            var emailExists = await _db.Users.AnyAsync(
                u => u.Email == email,
                cancellationToken);

            if (emailExists)
                throw new RegistrationConflictException(
                    "Email đã được sử dụng bởi tài khoản khác.");

            // 3. Tạo tài khoản Google lần đầu
            var fullName = string.IsNullOrWhiteSpace(payload.Name)
                ? email
                : payload.Name.Trim();

            user = new User
            {
                UserId = Guid.NewGuid().ToString(),
                Username = "google_" + Guid.NewGuid().ToString("N"),
                Email = email,
                FullName = fullName[..Math.Min(fullName.Length, 200)],
                GoogleId = googleId,
                PasswordHash = null,
                RoleId = "CANDIDATE",
                AccountStatus = AccountStatus.ACTIVE,
                CreatedAtUtc = DateTime.UtcNow
            };

            _db.Users.Add(user);

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
                when (ex.InnerException is SqlException sqlEx &&
                      (sqlEx.Number == 2601 || sqlEx.Number == 2627))
            {
                throw new RegistrationConflictException(
                    "Tài khoản Google hoặc email đã tồn tại.");
            }
        }

        // 4. Kiểm tra trạng thái tài khoản
        if (user.AccountStatus != AccountStatus.ACTIVE ||
            user.DeletedAtUtc != null)
        {
            throw new UnauthorizedAccessException(
                "Tài khoản không khả dụng.");
        }

        // 5. Cấp JWT nội bộ
        var (token, expiresAt) = _jwtTokenService.CreateToken(user);

        return new LoginResponse
        {
            AccessToken = token,
            TokenType = "Bearer",
            ExpiresAtUtc = expiresAt,
            UserId = user.UserId,
            Username = user.Username,
            Role = user.RoleId
        };
    }

}
