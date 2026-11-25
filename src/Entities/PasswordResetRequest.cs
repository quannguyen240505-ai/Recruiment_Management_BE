namespace Recruitment.Api.Entities;

public sealed class PasswordResetRequest
{
    public string ResetRequestId { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public string PasswordHashAtIssue { get; set; } = string.Empty;
    public string EmailAtIssue { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    // Outbox payload is protected by ASP.NET Data Protection, never stored as raw token.
    public string? ProtectedToken { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
    public int DeliveryAttempts { get; set; }
    public DateTime NextDeliveryAtUtc { get; set; }
}
