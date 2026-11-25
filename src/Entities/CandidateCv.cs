namespace Recruitment.Api.Entities;

// One immutable file version per record. Replacing or deleting does not remove
// prior files, so future Applications can continue to reference their CV.
public class CandidateCv
{
    public string CvId { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public User User { get; set; } = null!;
    public string OriginalFileName { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAtUtc { get; set; }
    public string? ReplacedByCvId { get; set; }
}
