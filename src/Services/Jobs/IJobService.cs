using Recruitment.Api.Dtos;
using Recruitment.Api.Dtos.Job;
using Recruitment.Api.Entities;

namespace Recruitment.Api.Services.Jobs;

public interface IJobService
{
    Task<JobResponse> CreateJobAsync(CreateJobRequest request, string recruiterId);
    Task<JobDetailResponse?> GetJobDetailAsync(string jobId);
    Task<JobDetailResponse?> GetRecruiterJobDetailAsync(string jobId, string recruiterId);
    Task<PagedResult<JobSummaryResponse>> GetMyJobsAsync(string recruiterId, JobStatus? status, int page, int pageSize);
    Task<SearchJobsResponse> SearchJobsAsync(SearchJobsRequest request);
    Task<JobResponse> UpdateJobAsync(
        string jobId,
        UpdateJobRequest request,
        string recruiterId);
}
