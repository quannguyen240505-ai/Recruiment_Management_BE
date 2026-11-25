using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Recruitment.Api.Data;
using Recruitment.Api.Dtos.Auth;
using Recruitment.Api.Entities;

namespace Recruitment.Api.Services.Auth;

public sealed class InvalidPasswordResetException : Exception { }
public sealed class PasswordResetUnavailableException : Exception { }

public interface IPasswordResetService
{
    Task RequestAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default);
    Task ResetAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
}

public sealed class PasswordResetService(
    AppDbContext db, IPasswordHasher<User> hasher,
    IDataProtectionProvider protection, IPasswordResetEmailSender sender,
    TimeProvider clock, IOptions<PasswordResetOptions> options) : IPasswordResetService
{
    private readonly IDataProtector _protector = protection.CreateProtector("Recruitment.PasswordReset.Outbox.v1");

    public async Task RequestAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        Validator.ValidateObject(request, new ValidationContext(request), true);
        // Configuration failure has the same response for every email, including unknown ones.
        if (!sender.IsConfigured) throw new PasswordResetUnavailableException();
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(
            u => u.Email.ToLower() == email, cancellationToken);
        // Google-only accounts use Google recovery, rather than silently gaining a local password.
        if (user == null || user.AccountStatus != AccountStatus.ACTIVE ||
            user.DeletedAtUtc != null || string.IsNullOrEmpty(user.PasswordHash)) return;

        var now = clock.GetUtcNow().UtcDateTime;
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        db.PasswordResetRequests.Add(new PasswordResetRequest
        {
            UserId = user.UserId, EmailAtIssue = user.Email,
            PasswordHashAtIssue = user.PasswordHash,
            TokenHash = HashToken(token), ProtectedToken = _protector.Protect(token),
            CreatedAtUtc = now, ExpiresAtUtc = now.AddMinutes(options.Value.TokenLifetimeMinutes),
            NextDeliveryAtUtc = now
        });
        // Email is sent by the outbox worker. Both known/unknown accounts return the same 202.
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ResetAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        Validator.ValidateObject(request, new ValidationContext(request), true);
        var now = clock.GetUtcNow().UtcDateTime;
        var hash = HashToken(request.Token);
        var reset = await db.PasswordResetRequests.AsNoTracking().SingleOrDefaultAsync(
            r => r.TokenHash == hash, cancellationToken);
        if (reset == null || reset.ConsumedAtUtc != null || reset.ExpiresAtUtc <= now)
            throw new InvalidPasswordResetException();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(
            u => u.UserId == reset.UserId, cancellationToken);
        if (user == null || user.AccountStatus != AccountStatus.ACTIVE || user.DeletedAtUtc != null ||
            user.PasswordHash != reset.PasswordHashAtIssue || user.Email != reset.EmailAtIssue)
            throw new InvalidPasswordResetException();
        if (hasher.VerifyHashedPassword(user, user.PasswordHash!, request.NewPassword)
            != PasswordVerificationResult.Failed)
            throw new ValidationException("Mật khẩu mới phải khác mật khẩu hiện tại.");

        var newHash = hasher.HashPassword(user, request.NewPassword);
        now = clock.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var consumed = await db.PasswordResetRequests.Where(r => r.ResetRequestId == reset.ResetRequestId &&
                r.ConsumedAtUtc == null && r.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ConsumedAtUtc, now)
                .SetProperty(r => r.ProtectedToken, (string?)null), cancellationToken);
        if (consumed != 1) throw new InvalidPasswordResetException();
        // Compare-and-set also invalidates sibling tokens when any password change wins the race.
        var updated = await db.Users.Where(u => u.UserId == reset.UserId &&
                u.PasswordHash == reset.PasswordHashAtIssue && u.Email == reset.EmailAtIssue &&
                u.AccountStatus == AccountStatus.ACTIVE && u.DeletedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.PasswordHash, newHash)
                .SetProperty(u => u.UpdatedAtUtc, now), cancellationToken);
        if (updated != 1) throw new InvalidPasswordResetException();
        await db.PasswordResetRequests.Where(r => r.UserId == reset.UserId && r.ConsumedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ConsumedAtUtc, now)
                .SetProperty(r => r.ProtectedToken, (string?)null), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    internal static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
