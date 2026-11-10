namespace Recruitment.Api.Dtos.Job;

public class SearchJobsResponse
{
    public List<SearchJobItemResponse> Items { get; set; } = new();

    public int Page { get; set; }
    public int Size { get; set; }
    public int TotalItems { get; set; }
}

public class SearchJobItemResponse
{
    public string JobId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string CompanyId { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string CategoryId { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string JobType { get; set; } = string.Empty;
    public string Experience { get; set; } = string.Empty;

    public decimal? SalaryMin { get; set; }
    public decimal? SalaryMax { get; set; }

    public DateTime Deadline { get; set; }
    public DateTime CreatedAt { get; set; }
}
