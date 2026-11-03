
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

    public AuthService(
        AppDbContext db,
        IPasswordHasher<User> passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
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
}
