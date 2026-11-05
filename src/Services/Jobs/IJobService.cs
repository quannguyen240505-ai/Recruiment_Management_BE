using Recruitment.Api.Dtos.Job;

namespace Recruitment.Api.Services.Jobs;

public interface IJobService
{
    Task<JobResponse> CreateJobAsync(CreateJobRequest request, string recruiterId);
    Task<JobDetailResponse?> GetJobDetailAsync(string jobId);
}
