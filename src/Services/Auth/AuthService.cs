
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

    public AuthService(
        AppDbContext db,
        IPasswordHasher<User> passwordHasher,
        JwtTokenService jwtTokenService)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
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

}
