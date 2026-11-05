namespace Recruitment.Api.Dtos.Job;

public class JobSummaryResponse
{
    public string JobId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public DateTime Deadline { get; set; }
    public DateTime CreatedAt { get; set; }
}
