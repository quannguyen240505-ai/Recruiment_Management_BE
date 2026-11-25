using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Recruitment.Api.Data;
using Recruitment.Api.Entities;

namespace Recruitment.Api.Services.Auth;

public sealed class PasswordResetDeliveryService(
    AppDbContext db, IDataProtectionProvider protection,
    IPasswordResetEmailSender sender, TimeProvider clock,
    ILogger<PasswordResetDeliveryService> logger)
{
    public async Task DeliverPendingAsync(CancellationToken cancellationToken)
    {
        if (!sender.IsConfigured) return;
        var now = clock.GetUtcNow().UtcDateTime;
        // Bound retries and remove protected payloads after expiry/consumption.
        await db.PasswordResetRequests.Where(r => r.ProtectedToken != null &&
                (r.ExpiresAtUtc <= now || r.ConsumedAtUtc != null || r.DeliveryAttempts >= 5))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ProtectedToken, (string?)null), cancellationToken);
        var pending = await db.PasswordResetRequests.Where(r => r.ProtectedToken != null &&
                r.DeliveredAtUtc == null && r.ConsumedAtUtc == null && r.ExpiresAtUtc > now &&
                r.NextDeliveryAtUtc <= now && r.DeliveryAttempts < 5)
            .OrderBy(r => r.CreatedAtUtc).Take(10).ToListAsync(cancellationToken);
        var protector = protection.CreateProtector("Recruitment.PasswordReset.Outbox.v1");
        foreach (var request in pending)
        {
            var eligible = await db.Users.AsNoTracking().AnyAsync(u => u.UserId == request.UserId &&
                u.AccountStatus == AccountStatus.ACTIVE && u.DeletedAtUtc == null &&
                u.Email == request.EmailAtIssue && u.PasswordHash == request.PasswordHashAtIssue,
                cancellationToken);
            if (!eligible) request.ProtectedToken = null;
            else
            {
                try
                {
                    await sender.SendAsync(request.EmailAtIssue, protector.Unprotect(request.ProtectedToken!),
                        cancellationToken);
                    request.DeliveredAtUtc = clock.GetUtcNow().UtcDateTime;
                    request.ProtectedToken = null;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception)
                {
                    // Do not log exceptions, recipient or payload: SMTP errors may contain PII.
                    logger.LogWarning("Password reset email delivery failed; retry scheduled.");
                    request.DeliveryAttempts++;
                    request.NextDeliveryAtUtc = clock.GetUtcNow().UtcDateTime.AddMinutes(1);
                    if (request.DeliveryAttempts >= 5) request.ProtectedToken = null;
                }
            }
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}

public sealed class PasswordResetDeliveryWorker(IServiceScopeFactory scopes,
    ILogger<PasswordResetDeliveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<PasswordResetDeliveryService>()
                    .DeliverPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                logger.LogWarning("Password reset outbox unavailable; will retry.");
            }
        }
    }
}
