using Microsoft.Extensions.Configuration;
using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Recruitment.Api.Controllers;
using Recruitment.Api.Data;
using Recruitment.Api.Dtos.Auth;
using Recruitment.Api.Entities;
using Recruitment.Api.Services.Auth;

namespace Recruitment.Api.Tests;

public sealed class PasswordResetTests
{
    [Fact]
    public async Task Request_KnownAndUnknownEmail_HaveSame202Body_AndNoRawTokenInDatabase()
    {
        await using var f = await Fixture.Create();
        var controller = new PasswordResetController(f.Service());
        var known = Assert.IsType<AcceptedResult>(await controller.ForgotPassword(
            new() { Email = " LOCAL@EXAMPLE.COM ".Trim() }, default));
        var unknown = Assert.IsType<AcceptedResult>(await controller.ForgotPassword(
            new() { Email = "unknown@example.com" }, default));
        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(known.Value),
            System.Text.Json.JsonSerializer.Serialize(unknown.Value));
        var stored = await f.Db.PasswordResetRequests.AsNoTracking().SingleAsync();
        Assert.Equal(64, stored.TokenHash.Length);
        Assert.Null(stored.DeliveredAtUtc);
        Assert.Empty(f.Sender.Messages);
        var encrypted = stored.ProtectedToken;
        await f.Deliver();
        var token = Assert.Single(f.Sender.Messages).Token;
        Assert.Equal(43, token.Length);
        Assert.DoesNotContain(token, encrypted!);
        Assert.NotEqual(token, stored.TokenHash);
        Assert.DoesNotContain(token, System.Text.Json.JsonSerializer.Serialize(known.Value));
        var sent = await f.Db.PasswordResetRequests.AsNoTracking().SingleAsync();
        Assert.NotNull(sent.DeliveredAtUtc);
        Assert.Null(sent.ProtectedToken);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("locked")]
    [InlineData("disabled")]
    [InlineData("deleted")]
    [InlineData("google-only")]
    public async Task Request_IneligibleAccount_DoesNotQueueEmail(string condition)
    {
        await using var f = await Fixture.Create();
        var user = await f.Db.Users.SingleAsync();
        if (condition == "locked") user.AccountStatus = AccountStatus.LOCKED;
        if (condition == "disabled") user.AccountStatus = AccountStatus.DISABLED;
        if (condition == "deleted") user.DeletedAtUtc = f.Clock.Now.UtcDateTime;
        if (condition == "google-only") { user.PasswordHash = null; user.GoogleId = "google-only"; }
        await f.Db.SaveChangesAsync();
        var response = await new PasswordResetController(f.Service()).ForgotPassword(
            new() { Email = condition == "unknown" ? "unknown@example.com" : "local@example.com" }, default);
        Assert.IsType<AcceptedResult>(response);
        Assert.Empty(await f.Db.PasswordResetRequests.ToListAsync());
    }

    [Theory]
    [InlineData("local@example.com")]
    [InlineData("unknown@example.com")]
    public async Task Request_UnconfiguredEmailChannel_Returns503ForEveryAccount(string email)
    {
        await using var f = await Fixture.Create();
        f.Sender.IsConfigured = false;
        var result = Assert.IsType<ObjectResult>(await new PasswordResetController(f.Service())
            .ForgotPassword(new() { Email = email }, default));
        Assert.Equal(503, result.StatusCode);
        Assert.Empty(await f.Db.PasswordResetRequests.ToListAsync());
    }

    [Fact]
    public async Task Reset_ValidToken_ChangesPassword_ConsumesAllTokens_AndPreservesRole()
    {
        await using var f = await Fixture.Create();
        var token = await f.IssueToken();
        var sibling = await f.IssueToken();
        var oldHash = (await f.Db.Users.AsNoTracking().SingleAsync()).PasswordHash;
        var response = await new PasswordResetController(f.Service()).ResetPassword(f.Request(token), default);
        Assert.IsType<NoContentResult>(response);
        var user = await f.Db.Users.AsNoTracking().SingleAsync();
        Assert.Equal("local-user", user.UserId);
        Assert.Equal("RECRUITER", user.RoleId);
        Assert.NotEqual(oldHash, user.PasswordHash);
        Assert.NotEqual(Fixture.NewPassword, user.PasswordHash);
        Assert.All(await f.Db.PasswordResetRequests.AsNoTracking().ToListAsync(), r =>
        { Assert.NotNull(r.ConsumedAtUtc); Assert.Null(r.ProtectedToken); });
        await Assert.ThrowsAsync<InvalidPasswordResetException>(() => f.Service().ResetAsync(f.Request(token)));
        await Assert.ThrowsAsync<InvalidPasswordResetException>(() => f.Service().ResetAsync(f.Request(sibling)));
        var auth = new AuthService(f.Db, f.Hasher, f.Jwt());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => auth.LoginAsync(
            new() { Username = "local", Password = Fixture.OldPassword }));
        Assert.Equal(user.UserId, (await auth.LoginAsync(
            new() { Username = "local", Password = Fixture.NewPassword })).UserId);
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData("expired")]
    [InlineData("consumed")]
    [InlineData("locked")]
    [InlineData("disabled")]
    [InlineData("deleted")]
    [InlineData("email-changed")]
    [InlineData("password-changed")]
    public async Task Reset_InvalidOrStaleToken_DoesNotChangePassword(string condition)
    {
        await using var f = await Fixture.Create();
        var token = await f.IssueToken();
        var user = await f.Db.Users.SingleAsync();
        if (condition == "wrong") token = new string('X', 43);
        if (condition == "expired") f.Clock.Now = f.Clock.Now.AddMinutes(15);
        if (condition == "consumed") (await f.Db.PasswordResetRequests.SingleAsync()).ConsumedAtUtc = f.Clock.Now.UtcDateTime;
        if (condition == "locked") user.AccountStatus = AccountStatus.LOCKED;
        if (condition == "disabled") user.AccountStatus = AccountStatus.DISABLED;
        if (condition == "deleted") user.DeletedAtUtc = f.Clock.Now.UtcDateTime;
        if (condition == "email-changed") user.Email = "new@example.com";
        if (condition == "password-changed") user.PasswordHash = f.Hasher.HashPassword(user, "OtherPassword123!");
        await f.Db.SaveChangesAsync();
        var previous = user.PasswordHash;
        var result = Assert.IsType<ObjectResult>(await new PasswordResetController(f.Service())
            .ResetPassword(f.Request(token), default));
        Assert.Equal(400, result.StatusCode);
        Assert.Equal(previous, (await f.Db.Users.AsNoTracking().SingleAsync()).PasswordHash);
    }

    [Theory]
    [InlineData("short", "short")]
    [InlineData("NewPassword123!", "Different123!")]
    [InlineData("OriginalPassword123!", "OriginalPassword123!")]
    public async Task Reset_InvalidPassword_DoesNotConsumeToken(string password, string confirmation)
    {
        await using var f = await Fixture.Create();
        var token = await f.IssueToken();
        var request = f.Request(token);
        request.NewPassword = password;
        request.ConfirmPassword = confirmation;
        await Assert.ThrowsAsync<ValidationException>(() => f.Service().ResetAsync(request));
        Assert.Null((await f.Db.PasswordResetRequests.AsNoTracking().SingleAsync()).ConsumedAtUtc);
        Assert.Equal(PasswordVerificationResult.Success, f.Hasher.VerifyHashedPassword(
            await f.Db.Users.AsNoTracking().SingleAsync(),
            (await f.Db.Users.AsNoTracking().SingleAsync()).PasswordHash!, Fixture.OldPassword));
    }

    [Fact]
    public async Task Delivery_SmtpFailure_IsRetried_WithoutChangingPassword()
    {
        await using var f = await Fixture.Create();
        await f.Service().RequestAsync(new() { Email = "local@example.com" });
        f.Sender.Fail = true;
        await f.Deliver();
        var reset = await f.Db.PasswordResetRequests.AsNoTracking().SingleAsync();
        Assert.Equal(1, reset.DeliveryAttempts);
        Assert.Null(reset.DeliveredAtUtc);
        Assert.NotNull(reset.ProtectedToken);
        f.Sender.Fail = false;
        await f.Deliver();
        Assert.Empty(f.Sender.Messages); // not due yet
        f.Clock.Now = f.Clock.Now.AddMinutes(1);
        await f.Deliver();
        Assert.Single(f.Sender.Messages);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("locked")]
    [InlineData("changed")]
    public async Task Delivery_ObsoleteRequests_AreNotSent(string condition)
    {
        await using var f = await Fixture.Create();
        await f.Service().RequestAsync(new() { Email = "local@example.com" });
        if (condition == "expired") f.Clock.Now = f.Clock.Now.AddMinutes(15);
        else
        {
            var user = await f.Db.Users.SingleAsync();
            if (condition == "locked") user.AccountStatus = AccountStatus.LOCKED;
            else user.PasswordHash = "new-hash";
            await f.Db.SaveChangesAsync();
        }
        await f.Deliver();
        Assert.Empty(f.Sender.Messages);
        Assert.Null((await f.Db.PasswordResetRequests.AsNoTracking().SingleAsync()).ProtectedToken);
    }

    [Fact]
    public async Task Reset_DatabaseFailure_RollsBackTokenConsumptionAndPassword()
    {
        await using var f = await Fixture.Create();
        var token = await f.IssueToken();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(f.Connection).AddInterceptors(new FailPasswordWrite()).Options);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service(db).ResetAsync(f.Request(token)));
        Assert.Null((await f.Db.PasswordResetRequests.AsNoTracking().SingleAsync()).ConsumedAtUtc);
        await f.Service().ResetAsync(f.Request(token)); // rollback left token usable
    }

    [Fact]
    public async Task Reset_TwoRequestsWithSameToken_OnlyOneCanCommit()
    {
        await using var f = await Fixture.Create();
        var token = await f.IssueToken();
        var interceptor = new BeforeTransaction(async () =>
            await f.Service().ResetAsync(f.Request(token)));
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(f.Connection).AddInterceptors(interceptor).Options);
        await Assert.ThrowsAsync<InvalidPasswordResetException>(() => f.Service(db).ResetAsync(f.Request(token)));
        Assert.True(interceptor.Ran);
        Assert.NotNull((await f.Db.PasswordResetRequests.AsNoTracking().SingleAsync()).ConsumedAtUtc);
    }

    private sealed class BeforeTransaction(Func<Task> competingReset) : DbTransactionInterceptor
    {
        public bool Ran { get; private set; }
        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            if (!Ran) { Ran = true; await competingReset(); }
            return result;
        }
    }

    private sealed class FailPasswordWrite : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE \"Users\""))
                throw new InvalidOperationException("Injected DB write failure");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 11, 25, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeSender : IPasswordResetEmailSender
    {
        public bool IsConfigured { get; set; } = true;
        public bool Fail { get; set; }
        public List<(string Email, string Token)> Messages { get; } = [];
        public Task SendAsync(string email, string token, CancellationToken cancellationToken)
        {
            if (Fail) throw new InvalidOperationException("Simulated SMTP failure");
            Messages.Add((email, token));
            return Task.CompletedTask;
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public const string OldPassword = "OriginalPassword123!";
        public const string NewPassword = "NewPassword456!";
        public SqliteConnection Connection { get; } = new("Data Source=:memory:");
        public AppDbContext Db { get; private set; } = null!;
        public PasswordHasher<User> Hasher { get; } = new();
        public IDataProtectionProvider Protection { get; } = new EphemeralDataProtectionProvider();
        public FakeSender Sender { get; } = new();
        public FakeClock Clock { get; } = new();
        public static async Task<Fixture> Create()
        {
            var f = new Fixture();
            await f.Connection.OpenAsync();
            f.Db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(f.Connection).Options);
            await f.Db.Database.EnsureCreatedAsync();
            var user = new User { UserId = "local-user", Username = "local", Email = "local@example.com",
                FullName = "Local User", RoleId = "RECRUITER", AccountStatus = AccountStatus.ACTIVE };
            user.PasswordHash = f.Hasher.HashPassword(user, OldPassword);
            f.Db.Users.Add(user);
            await f.Db.SaveChangesAsync();
            f.Db.ChangeTracker.Clear();
            return f;
        }
        public PasswordResetService Service(AppDbContext? db = null) =>
            new(db ?? Db, Hasher, Protection, Sender, Clock, Options.Create(new PasswordResetOptions()));
        public async Task Deliver()
        {
            await new PasswordResetDeliveryService(Db, Protection, Sender, Clock,
                NullLogger<PasswordResetDeliveryService>.Instance).DeliverPendingAsync(default);
        }
        public async Task<string> IssueToken()
        {
            await Service().RequestAsync(new() { Email = "local@example.com" });
            await Deliver();
            return Sender.Messages.Last().Token;
        }
        public ResetPasswordRequest Request(string token) => new()
        { Token = token, NewPassword = NewPassword, ConfirmPassword = NewPassword };
        public JwtTokenService Jwt() => new(new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "0123456789abcdef0123456789abcdef",
                ["Jwt:Issuer"] = "RecruitmentApi", ["Jwt:Audience"] = "RecruitmentClient"
            }).Build());
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }
}
