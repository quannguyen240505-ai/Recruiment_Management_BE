using Recruitment.Api.Entities;

namespace Recruitment.Api.Dtos.Job;

public class SearchJobsRequest
{
    public string? Keyword { get; set; }
    public string? Location { get; set; }
    public JobType? JobType { get; set; }
    public string? Experience { get; set; }
    public decimal? SalaryMin { get; set; }
    public string? SkillId { get; set; }
    public string? CategoryId { get; set; }
    public string? CompanyId { get; set; }

    public int Page { get; set; } = 0;
    public int Size { get; set; } = 20;
}
