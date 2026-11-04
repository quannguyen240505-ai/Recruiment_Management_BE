namespace Recruitment.Api.Entities;

public class Job
{
    public string JobId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Requirements { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public JobType JobType { get; set; }
    public string Experience { get; set; } = string.Empty;
    public int NumberOfPositions { get; set; }
    public decimal? SalaryMin { get; set; }
    public decimal? SalaryMax { get; set; }
    public DateTime Deadline { get; set; }
    public JobStatus Status { get; set; } = JobStatus.OPEN;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Foreign Keys
    public string CategoryId { get; set; } = string.Empty;
    public Category Category { get; set; } = null!;

    public string RecruiterId { get; set; } = string.Empty;
    public User Recruiter { get; set; } = null!;

    public string CompanyId { get; set; } = string.Empty;
    public Company Company { get; set; } = null!;

    public ICollection<JobSkill> JobSkills { get; set; } = new List<JobSkill>();
}
