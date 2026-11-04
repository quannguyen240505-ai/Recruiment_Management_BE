
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Recruitment.Api.Data;
using Recruitment.Api.Dtos.Auth;
using Recruitment.Api.Entities;
using Recruitment.Api.Services.Auth;
using Xunit;

namespace Recruitment.Api.Tests;

public class AuthServiceTests
{
    // TEST 1: Đăng ký tài khoản
    [Fact]
    public async Task Register_ShouldHashPasswordAndAssignCandidateRole()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var hasher = new PasswordHasher<User>();
        var configuration = new ConfigurationBuilder().Build();
        var jwtService = new JwtTokenService(configuration);
        var service = new AuthService(db, hasher, jwtService);

        var request = new RegisterRequest
        {
            FullName = "Nguyen Van A",
            Username = "testuser",
            Email = "testuser@example.com",
            PhoneNumber = "0912345678",
            Password = "Test@123456",
            ConfirmPassword = "Test@123456"
        };

        var response = await service.RegisterAsync(request);

        var user = await db.Users.SingleAsync(
            u => u.UserId == response.UserId);

        Assert.NotNull(user.PasswordHash);
        Assert.NotEqual(request.Password, user.PasswordHash);
        Assert.Equal("CANDIDATE", user.RoleId);

        var result = hasher.VerifyHashedPassword(
            user, user.PasswordHash!, request.Password);

        Assert.Equal(PasswordVerificationResult.Success, result);
    }

    // TEST 2: Đăng nhập thành công và nhận JWT
    [Fact]
    public async Task Login_ValidCredentials_ShouldReturnJwt()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "0123456789abcdef0123456789abcdef",
                ["Jwt:Issuer"] = "RecruitmentApi",
                ["Jwt:Audience"] = "RecruitmentClient"
            })
            .Build();

        var hasher = new PasswordHasher<User>();
        var jwtService = new JwtTokenService(configuration);
        var service = new AuthService(db, hasher, jwtService);

        await service.RegisterAsync(new RegisterRequest
        {
            FullName = "Nguyen Van A",
            Username = "testuser",
            Email = "testuser@example.com",
            PhoneNumber = "0912345678",
            Password = "Test@123456",
            ConfirmPassword = "Test@123456"
        });

        var response = await service.LoginAsync(new LoginRequest
        {
            Username = "testuser",
            Password = "Test@123456"
        });

        Assert.False(string.IsNullOrWhiteSpace(response.AccessToken));
        Assert.Equal("Bearer", response.TokenType);
        Assert.Equal("testuser", response.Username);
        Assert.Equal("CANDIDATE", response.Role);
        Assert.True(response.ExpiresAtUtc > DateTime.UtcNow);
    }

    [Theory]
    [InlineData("testuser", "Wrong@123", false)]
    [InlineData("notexist", "Test@123456", false)]
    [InlineData("testuser@example.com", "Test@123456", false)]
    [InlineData("testuser", "Test@123456", true)]
    public async Task Login_InvalidCredentialsOrLocked_ShouldReject(
        string username, string password, bool locked)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "0123456789abcdef0123456789abcdef",
                ["Jwt:Issuer"] = "RecruitmentApi",
                ["Jwt:Audience"] = "RecruitmentClient"
            })
            .Build();

        var hasher = new PasswordHasher<User>();
        var jwtService = new JwtTokenService(configuration);
        var service = new AuthService(db, hasher, jwtService);

        await service.RegisterAsync(new RegisterRequest
        {
            FullName = "Nguyen Van A",
            Username = "testuser",
            Email = "testuser@example.com",
            PhoneNumber = "0912345678",
            Password = "Test@123456",
            ConfirmPassword = "Test@123456"
        });

        if (locked)
        {
            var user = await db.Users.SingleAsync();
            user.AccountStatus = AccountStatus.LOCKED;
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.LoginAsync(new LoginRequest
            {
                Username = username,
                Password = password
            }));
    }

}
