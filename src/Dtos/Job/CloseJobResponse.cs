
namespace Recruitment.Api.Dtos.Job;

public class CloseJobResponse
{
    public string JobId { get; set; } = string.Empty;
    public string Status { get; set; } = "CLOSED";
    public bool AlreadyClosed { get; set; }
}
