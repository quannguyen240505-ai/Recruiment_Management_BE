using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

namespace Recruitment.Api.Entities;

// Thêm enum cho AccountStatus
public enum AccountStatus
{
    ACTIVE,
    LOCKED,
    DISABLED
}

public class User
{
    public string UserId { get; set; } = string.Empty;

    public string RoleId { get; set; } = string.Empty;
    public Role Role { get; set; } = null!;

    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    public string? PasswordHash { get; set; }
    public string? GoogleId { get; set; }

    public AccountStatus AccountStatus { get; set; } = AccountStatus.ACTIVE;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public string? CompanyId { get; set; }
    public Company? Company { get; set; }

}
