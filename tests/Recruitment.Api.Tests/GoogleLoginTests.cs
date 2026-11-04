
using Google.Apis.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Recruitment.Api.Data;
using Recruitment.Api.Dtos.Auth;
using Recruitment.Api.Entities;
using Recruitment.Api.Services.Auth;

namespace Recruitment.Api.Tests;

public class GoogleLoginTests
{
    [Fact]
    public async Task GoogleLogin_FirstTime_ShouldCreateCandidateAndReturnJwt()
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

        var googlePayload = new GoogleJsonWebSignature.Payload
        {
            Subject = "google-user-123",
            Email = "TEST@GMAIL.COM",
            EmailVerified = true,
            Name = "Nguyen Van A"
        };

        var service = new AuthService(
            db,
            new PasswordHasher<User>(),
            new JwtTokenService(configuration),
            new FakeGoogleTokenValidator(googlePayload));

        var result = await service.GoogleLoginAsync(
            new GoogleLoginRequest { IdToken = "fake-test-token" });

        var user = await db.Users.SingleAsync();

        Assert.Equal("google-user-123", user.GoogleId);
        Assert.Equal("test@gmail.com", user.Email);
        Assert.Equal("CANDIDATE", user.RoleId);
        Assert.Equal(AccountStatus.ACTIVE, user.AccountStatus);
        Assert.Null(user.PasswordHash);

        Assert.Equal(user.UserId, result.UserId);
        Assert.Equal("CANDIDATE", result.Role);
        Assert.False(string.IsNullOrWhiteSpace(result.AccessToken));
        Assert.Equal("Bearer", result.TokenType);
    }

    [Fact]
    public async Task GoogleLogin_ExistingGoogleUser_ShouldNotCreateDuplicate()
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

        var payload = new GoogleJsonWebSignature.Payload
        {
            Subject = "google-user-123",
            Email = "test@gmail.com",
            EmailVerified = true,
            Name = "Nguyen Van A"
        };

        var service = new AuthService(
            db,
            new PasswordHasher<User>(),
            new JwtTokenService(configuration),
            new FakeGoogleTokenValidator(payload));

        var request = new GoogleLoginRequest
        {
            IdToken = "fake-test-token"
        };

        // Đăng nhập lần đầu
        var firstLogin = await service.GoogleLoginAsync(request);

        // Đăng nhập lần hai
        var secondLogin = await service.GoogleLoginAsync(request);

        // Vẫn là tài khoản cũ
        Assert.Equal(firstLogin.UserId, secondLogin.UserId);

        // Database chỉ có một tài khoản
        Assert.Equal(1, await db.Users.CountAsync());

        // Lần đăng nhập thứ hai vẫn cấp JWT
        Assert.False(string.IsNullOrWhiteSpace(secondLogin.AccessToken));
    }

    [Fact]
    public async Task GoogleLogin_EmailAlreadyExists_ShouldReject()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        // Tài khoản thường đã tồn tại
        db.Users.Add(new User
        {
            UserId = Guid.NewGuid().ToString(),
            Username = "existinguser",
            Email = "existing@gmail.com",
            FullName = "Existing User",
            PasswordHash = "existing-password-hash",
            RoleId = "CANDIDATE"
        });

        await db.SaveChangesAsync();

        // Google trả về email trùng tài khoản thường
        var payload = new GoogleJsonWebSignature.Payload
        {
            Subject = "google-user-456",
            Email = "existing@gmail.com",
            EmailVerified = true,
            Name = "Google User"
        };

        var service = new AuthService(
            db,
            new PasswordHasher<User>(),
            new JwtTokenService(new ConfigurationBuilder().Build()),
            new FakeGoogleTokenValidator(payload));

        await Assert.ThrowsAsync<RegistrationConflictException>(
            () => service.GoogleLoginAsync(new GoogleLoginRequest
            {
                IdToken = "fake-test-token"
            }));

        // Không tạo mới hoặc tự liên kết tài khoản
        Assert.Equal(1, await db.Users.CountAsync());
        Assert.Null((await db.Users.SingleAsync()).GoogleId);
    }

    [Theory]
    [InlineData(AccountStatus.LOCKED)]
    [InlineData(AccountStatus.DISABLED)]
    public async Task GoogleLogin_InactiveAccount_ShouldReject(
        AccountStatus status)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        // Tài khoản Google đã tồn tại nhưng không còn hoạt động
        db.Users.Add(new User
        {
            UserId = Guid.NewGuid().ToString(),
            Username = "google_account",
            Email = "google@example.com",
            FullName = "Google User",
            GoogleId = "google-user-789",
            RoleId = "CANDIDATE",
            AccountStatus = status
        });

        await db.SaveChangesAsync();

        var payload = new GoogleJsonWebSignature.Payload
        {
            Subject = "google-user-789",
            Email = "google@example.com",
            EmailVerified = true
        };

        var service = new AuthService(
            db,
            new PasswordHasher<User>(),
            new JwtTokenService(new ConfigurationBuilder().Build()),
            new FakeGoogleTokenValidator(payload));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.GoogleLoginAsync(new GoogleLoginRequest
            {
                IdToken = "fake-test-token"
            }));

        Assert.Equal(1, await db.Users.CountAsync());
    }

    [Fact]
    public async Task GoogleTokenValidator_InvalidToken_ShouldReject()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Google:ClientId"] = "test-client-id.apps.googleusercontent.com"
            })
            .Build();

        var validator = new GoogleTokenValidator(configuration);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => validator.ValidateAsync("invalid-google-token"));
    }

    private sealed class FakeGoogleTokenValidator : IGoogleTokenValidator
    {
        private readonly GoogleJsonWebSignature.Payload _payload;

        public FakeGoogleTokenValidator(
            GoogleJsonWebSignature.Payload payload)
        {
            _payload = payload;
        }

        public Task<GoogleJsonWebSignature.Payload> ValidateAsync(
            string idToken)
        {
            return Task.FromResult(_payload);
        }
    }
}
