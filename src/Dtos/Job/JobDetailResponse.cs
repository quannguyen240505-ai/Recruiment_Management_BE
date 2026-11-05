
namespace Recruitment.Api.Dtos.Job;

public class JobDetailResponse
{
    public string JobId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Requirements { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string JobType { get; set; } = string.Empty;
    public string Experience { get; set; } = string.Empty;
    public int NumberOfPositions { get; set; }
    public decimal? SalaryMin { get; set; }
    public decimal? SalaryMax { get; set; }
    public DateTime Deadline { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public JobCompanyResponse Company { get; set; } = new();
    public List<JobSkillResponse> Skills { get; set; } = new();
}

public class JobCompanyResponse
{
    public string CompanyId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Website { get; set; }
    public string? Address { get; set; }
    public string? Description { get; set; }
}

public class JobSkillResponse
{
    public string SkillId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
