
using Google.Apis.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Recruitment.Api.Controllers;
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

    [Theory]
    [InlineData("CANDIDATE")]
    [InlineData("RECRUITER")]
    [InlineData("ADMIN")]
    public async Task GoogleLogin_LocalEmailMatch_ReusesAccountAndPreservesPassword(string role)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        var user = CreateLocalUser(role);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var originalHash = user.PasswordHash;
        var originalCreatedAt = user.CreatedAtUtc;
        db.ChangeTracker.Clear();
        var service = CreateService(db);

        var action = await new AuthController(service).GoogleLogin(
            GoogleRequest(), CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(action.Result);
        Assert.Equal(200, ok.StatusCode);
        var first = Assert.IsType<LoginResponse>(ok.Value);
        var second = await service.GoogleLoginAsync(GoogleRequest());
        var local = await service.LoginAsync(new LoginRequest
        {
            Username = "existinguser", Password = "OriginalPassword123!"
        });
        var saved = await db.Users.AsNoTracking().SingleAsync();

        Assert.Equal(user.UserId, first.UserId);
        Assert.Equal(first.UserId, second.UserId);
        Assert.Equal(first.UserId, local.UserId);
        Assert.Equal(role, first.Role);
        Assert.Equal(role, saved.RoleId);
        Assert.Equal(originalHash, saved.PasswordHash);
        Assert.Equal("Existing User", saved.FullName);
        Assert.Equal(user.Email, saved.Email);
        Assert.Equal(user.PhoneNumber, saved.PhoneNumber);
        Assert.Equal(originalCreatedAt, saved.CreatedAtUtc);
        Assert.Equal("google-user-456", saved.GoogleId);
        Assert.NotNull(saved.UpdatedAtUtc);
        Assert.False(string.IsNullOrWhiteSpace(first.AccessToken));
    }

    [Theory]
    [InlineData(AccountStatus.LOCKED, false)]
    [InlineData(AccountStatus.DISABLED, false)]
    [InlineData(AccountStatus.ACTIVE, true)]
    public async Task GoogleLogin_UnavailableLocalAccount_DoesNotLink(
        AccountStatus status, bool deleted)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        var user = CreateLocalUser();
        user.AccountStatus = status;
        user.DeletedAtUtc = deleted ? DateTime.UtcNow : null;
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CreateService(db).GoogleLoginAsync(GoogleRequest()));
        Assert.Null((await db.Users.AsNoTracking().SingleAsync()).GoogleId);
    }

    [Fact]
    public async Task GoogleLogin_LocalAccountLinkedToDifferentGoogle_DoesNotOverwrite()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        var user = CreateLocalUser();
        user.GoogleId = "different-google-subject";
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<RegistrationConflictException>(() =>
            CreateService(db).GoogleLoginAsync(GoogleRequest()));
        Assert.Equal("different-google-subject",
            (await db.Users.AsNoTracking().SingleAsync()).GoogleId);
    }

    [Fact]
    public async Task GoogleLogin_UnverifiedClaims_DoNotLinkLocalAccount()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(CreateLocalUser());
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CreateService(db, verified: false).GoogleLoginAsync(GoogleRequest()));
        Assert.Null((await db.Users.AsNoTracking().SingleAsync()).GoogleId);
    }

    [Theory]
    [InlineData("same")]
    [InlineData("different")]
    [InlineData("locked")]
    [InlineData("deleted")]
    public async Task GoogleLogin_ConcurrentChangeBeforeLink_IsNotOverwritten(string change)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var competingDb = CreateDb(connection);
        await competingDb.Database.EnsureCreatedAsync();
        var user = CreateLocalUser();
        competingDb.Users.Add(user);
        await competingDb.SaveChangesAsync();
        competingDb.ChangeTracker.Clear();

        // Simulate another request committing after lookup but before conditional UPDATE.
        var interceptor = new BeforeLinkInterceptor(async () =>
        {
            if (change is "same" or "different")
                await competingDb.Users.ExecuteUpdateAsync(setters => setters
                    .SetProperty(u => u.GoogleId,
                        change == "same" ? "google-user-456" : "other-google-id"));
            else if (change == "locked")
                await competingDb.Users.ExecuteUpdateAsync(setters => setters
                    .SetProperty(u => u.AccountStatus, AccountStatus.LOCKED));
            else
                await competingDb.Users.ExecuteUpdateAsync(setters => setters
                    .SetProperty(u => u.DeletedAtUtc, DateTime.UtcNow));
        });
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection).AddInterceptors(interceptor).Options;
        await using var db = new AppDbContext(options);
        var service = CreateService(db);

        if (change == "same")
            Assert.Equal(user.UserId, (await service.GoogleLoginAsync(GoogleRequest())).UserId);
        else if (change == "different")
            await Assert.ThrowsAsync<RegistrationConflictException>(() =>
                service.GoogleLoginAsync(GoogleRequest()));
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.GoogleLoginAsync(GoogleRequest()));

        Assert.True(interceptor.Ran);
        var saved = await competingDb.Users.AsNoTracking().SingleAsync();
        Assert.Equal(user.PasswordHash, saved.PasswordHash);
        Assert.Equal(change == "same" ? "google-user-456" :
            change == "different" ? "other-google-id" : null, saved.GoogleId);
    }

    private static AppDbContext CreateDb(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);

    private static User CreateLocalUser(string role = "CANDIDATE")
    {
        var user = new User
        {
            UserId = Guid.NewGuid().ToString(),
            Username = "existinguser",
            Email = "Existing@Gmail.com",
            FullName = "Existing User",
            PhoneNumber = "0901234567",
            RoleId = role
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "OriginalPassword123!");
        return user;
    }

    private static GoogleLoginRequest GoogleRequest() => new() { IdToken = "fake-test-token" };

    private static AuthService CreateService(AppDbContext db, bool verified = true) =>
        new(db, new PasswordHasher<User>(),
            new JwtTokenService(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = "0123456789abcdef0123456789abcdef",
                    ["Jwt:Issuer"] = "RecruitmentApi",
                    ["Jwt:Audience"] = "RecruitmentClient"
                }).Build()),
            new FakeGoogleTokenValidator(new GoogleJsonWebSignature.Payload
            {
                Subject = "google-user-456", Email = "  EXISTING@gmail.com  ",
                EmailVerified = verified, Name = "Google User"
            }));

    private sealed class BeforeLinkInterceptor(Func<Task> beforeLink)
        : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        public bool Ran { get; private set; }

        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>>
            NonQueryExecutingAsync(System.Data.Common.DbCommand command,
                Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
                Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
        {
            if (!Ran && command.CommandText.StartsWith("UPDATE "))
            {
                Ran = true;
                await beforeLink();
            }
            return result;
        }
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

    [Fact]
    public async Task GoogleTokenValidator_MissingClientId_ShouldReject()
    {
        var configuration = new ConfigurationBuilder().Build();

        var validator = new GoogleTokenValidator(configuration);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => validator.ValidateAsync("fake-test-token"));

        Assert.Equal(
            "Google Client ID chưa được cấu hình.",
            exception.Message);
    }

    [Fact]
    public void GoogleTokenValidator_UnverifiedEmail_ShouldReject()
    {
        var payload = new GoogleJsonWebSignature.Payload
        {
            Subject = "google-user-123",
            Email = "test@gmail.com",
            EmailVerified = false
        };

        Assert.Throws<UnauthorizedAccessException>(
            () => GoogleTokenValidator.ValidateGoogleClaims(payload));
    }

    [Fact]
    public void GoogleTokenValidator_ExpiredToken_ShouldReject()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(2_000_000_000);

        var payload = new GoogleJsonWebSignature.Payload
        {
            Audience = "correct-client-id",
            ExpirationTimeSeconds = now.ToUnixTimeSeconds() - 1
        };

        Assert.Throws<UnauthorizedAccessException>(() =>
            GoogleTokenValidator.ValidateTokenMetadata(
                payload, "correct-client-id", now));
    }

    [Fact]
    public void GoogleTokenValidator_WrongAudience_ShouldReject()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(2_000_000_000);

        var payload = new GoogleJsonWebSignature.Payload
        {
            Audience = "other-client-id",
            ExpirationTimeSeconds = now.ToUnixTimeSeconds() + 3600
        };

        Assert.Throws<UnauthorizedAccessException>(() =>
            GoogleTokenValidator.ValidateTokenMetadata(
                payload, "correct-client-id", now));
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
