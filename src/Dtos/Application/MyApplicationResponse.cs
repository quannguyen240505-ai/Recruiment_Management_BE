
namespace Recruitment.Api.Dtos.Application;

// Thông tin một hồ sơ ứng tuyển
public class MyApplicationResponse
{
    public string ApplicationId { get; set; } = string.Empty;
    public string JobId { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string CvId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime AppliedAtUtc { get; set; }
}

// Kết quả danh sách có phân trang
public class MyApplicationsResponse
{
    public List<MyApplicationResponse> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

// Thông tin chi tiết hồ sơ
public class MyApplicationDetailResponse : MyApplicationResponse
{
    public string? CoverLetter { get; set; }
}
