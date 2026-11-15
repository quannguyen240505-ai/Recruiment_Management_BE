
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

        GoogleTokenValidator.ValidateGoogleClaims(payload);

        var user = await ResolveGoogleUserAsync(
            payload.Subject,
            payload.Email.Trim().ToLowerInvariant(),
            payload.Name,
            cancellationToken);

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

    private async Task<User> ResolveGoogleUserAsync(
        string googleId,
        string email,
        string? name,
        CancellationToken cancellationToken)
    {
        // Retry resolution after a competing insert wins a unique constraint.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var user = await _db.Users.AsNoTracking().SingleOrDefaultAsync(
                u => u.GoogleId == googleId, cancellationToken);

            if (user != null)
            {
                EnsureGoogleAccountAvailable(user);
                return user;
            }

            // Registration normalizes email; ToLower also covers legacy mixed-case rows.
            user = await _db.Users.AsNoTracking().SingleOrDefaultAsync(
                u => u.Email.ToLower() == email, cancellationToken);

            if (user != null)
            {
                EnsureGoogleAccountAvailable(user);
                if (user.GoogleId != null && user.GoogleId != googleId)
                    throw new RegistrationConflictException(
                        "Tài khoản đã liên kết với Google khác.");

                // Conditional update prevents concurrent requests overwriting another link.
                // PasswordHash, RoleId and profile data are never modified by linking.
                try
                {
                    await _db.Users
                        .Where(u => u.UserId == user.UserId &&
                                    u.GoogleId == null &&
                                    u.Email.ToLower() == email &&
                                    u.AccountStatus == AccountStatus.ACTIVE &&
                                    u.DeletedAtUtc == null)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(u => u.GoogleId, googleId)
                            .SetProperty(u => u.UpdatedAtUtc, DateTime.UtcNow),
                            cancellationToken);
                }
                catch (DbUpdateException ex) when (IsUniqueConflict(ex.InnerException))
                {
                    continue;
                }
                catch (SqlException ex) when (IsUniqueConflict(ex))
                {
                    continue;
                }

                user = await _db.Users.AsNoTracking().SingleAsync(
                    u => u.UserId == user.UserId, cancellationToken);
                EnsureGoogleAccountAvailable(user);
                if (user.GoogleId != googleId)
                    throw new RegistrationConflictException(
                        "Tài khoản đã liên kết với Google khác.");

                return user;
            }

            var fullName = string.IsNullOrWhiteSpace(name) ? email : name.Trim();
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
                return user;
            }
            catch (DbUpdateException ex)
                when (IsUniqueConflict(ex.InnerException))
            {
                _db.Entry(user).State = EntityState.Detached;
            }
        }

        throw new RegistrationConflictException(
            "Tài khoản Google hoặc email vừa được cập nhật. Vui lòng thử lại.");
    }

    private static bool IsUniqueConflict(Exception? exception) =>
        exception is SqlException { Number: 2601 or 2627 };

    private static void EnsureGoogleAccountAvailable(User user)
    {
        if (user.AccountStatus != AccountStatus.ACTIVE || user.DeletedAtUtc != null)
            throw new UnauthorizedAccessException("Tài khoản không khả dụng.");
    }

}
