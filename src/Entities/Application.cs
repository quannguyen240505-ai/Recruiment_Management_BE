namespace Recruitment.Api.Entities;

public class Application
{
    public string ApplicationId { get; set; } =
        Guid.NewGuid().ToString();

    public string CandidateId { get; set; } =
        string.Empty;

    public User Candidate { get; set; } = null!;

    public string JobId { get; set; } =
        string.Empty;

    public Job Job { get; set; } = null!;

    public string CvId { get; set; } =
        string.Empty;

    public string? CoverLetter { get; set; }

    public ApplicationStatus Status { get; set; } =
        ApplicationStatus.APPLIED;

    public DateTime AppliedAtUtc { get; set; } =
        DateTime.UtcNow;
}
