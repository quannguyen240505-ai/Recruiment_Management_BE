using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Recruitment.Api.Data;
using Recruitment.Api.Services.Auth;
using Microsoft.AspNetCore.Identity;
using Recruitment.Api.Entities;
using Recruitment.Api.Dtos.Auth;

namespace Recruitment.Api.Tests;

public sealed class PasswordResetHttpTests
{
    [Fact]
    public async Task AnonymousRequest_RunsThroughRealRoutingAndController_Returns202WithoutToken()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        await factory.CreateSchema();
        var response = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "unknown@example.com" });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("message", body);
        Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task ForgotResetLogin_FullFlow_ShouldSucceed()
    {
        using var factory = new ApiFactory();

        using var client = factory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });

        await factory.CreateSchema();

        const string oldPassword = "OriginalPassword123!";
        const string newPassword = "NewPassword456!";

        // Arrange: Create test account
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            var hasher = scope.ServiceProvider
                .GetRequiredService<IPasswordHasher<User>>();

            var user = new User
            {
                UserId = "reset-integration-user",
                Username = "resetintegration",
                Email = "reset@example.com",
                FullName = "Reset Integration",
                RoleId = "CANDIDATE",
                AccountStatus = AccountStatus.ACTIVE
            };

            user.PasswordHash =
                hasher.HashPassword(user, oldPassword);

            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        // Verify old password works before reset
        var originalLogin = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                username = "resetintegration",
                password = oldPassword
            });

        Assert.Equal(HttpStatusCode.OK,
            originalLogin.StatusCode);

        // Step 1: Request password reset
        var forgot = await client.PostAsJsonAsync(
            "/api/auth/forgot-password",
            new { email = "reset@example.com" });

        Assert.Equal(HttpStatusCode.Accepted,
            forgot.StatusCode);

        // Step 2: Deliver email using fake sender
        using (var scope = factory.Services.CreateScope())
        {
            var delivery = scope.ServiceProvider
                .GetRequiredService<PasswordResetDeliveryService>();

            await delivery.DeliverPendingAsync(default);
        }

        var sender = (ConfiguredSender)factory.Services
            .GetRequiredService<IPasswordResetEmailSender>();

        var token = Assert.Single(sender.Tokens);

        // Step 3: Reset password
        var resetRequest = new
        {
            token,
            newPassword,
            confirmPassword = newPassword
        };

        var reset = await client.PostAsJsonAsync(
            "/api/auth/reset-password",
            resetRequest);

        Assert.Equal(HttpStatusCode.NoContent,
            reset.StatusCode);

        // Step 4: Old password must be rejected
        var oldLogin = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                username = "resetintegration",
                password = oldPassword
            });

        Assert.Equal(HttpStatusCode.Unauthorized,
            oldLogin.StatusCode);

        // Step 5: New password must work
        var newLogin = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                username = "resetintegration",
                password = newPassword
            });

        Assert.Equal(HttpStatusCode.OK,
            newLogin.StatusCode);

        var loginData = await newLogin.Content
            .ReadFromJsonAsync<LoginResponse>();

        Assert.NotNull(loginData);
        Assert.False(
            string.IsNullOrWhiteSpace(loginData.AccessToken));

        // Step 6: Token cannot be reused
        var replay = await client.PostAsJsonAsync(
            "/api/auth/reset-password",
            resetRequest);

        Assert.Equal(HttpStatusCode.BadRequest,
            replay.StatusCode);
    }

    [Fact]
    public async Task InvalidDto_IsRejectedByApiController_WithoutAuthentication()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var request = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "invalid" });
        var reset = await client.PostAsJsonAsync("/api/auth/reset-password",
            new { token = "invalid", newPassword = "weak", confirmPassword = "other" });
        Assert.Equal(HttpStatusCode.BadRequest, request.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
    }

    [Fact]
    public async Task RepeatedRequests_Trigger429_FromRateLimiter()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        await factory.CreateSchema();
        for (var i = 0; i < 10; i++)
        {
            var allowed = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "unknown@example.com" });
            Assert.Equal(HttpStatusCode.Accepted, allowed.StatusCode);
        }
        var denied = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "unknown@example.com" });
        Assert.Equal(HttpStatusCode.TooManyRequests, denied.StatusCode);
    }

    private sealed class ApiFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        public ApiFactory() => _connection.Open();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Jwt:Key", "test-only-signing-key-0123456789abcdef");
            builder.UseSetting("Jwt:Issuer", "RecruitmentApi");
            builder.UseSetting("Jwt:Audience", "RecruitmentClient");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
                services.RemoveAll<IPasswordResetEmailSender>();
                services.AddSingleton<IPasswordResetEmailSender, ConfiguredSender>();
                var worker = services.Single(d => d.ImplementationType == typeof(PasswordResetDeliveryWorker));
                services.Remove(worker);
            });
        }
        public async Task CreateSchema()
        {
            using var scope = Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _connection.Dispose();
        }
    }

    private sealed class ConfiguredSender
    : IPasswordResetEmailSender
    {
        public bool IsConfigured => true;

        public List<string> Tokens { get; } = new();

        public Task SendAsync(
            string email,
            string token,
            CancellationToken cancellationToken)
        {
            Tokens.Add(token);

            return Task.CompletedTask;
        }
    }
}
