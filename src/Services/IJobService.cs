using Recruitment.Api.Dtos;

namespace Recruitment.Api.Services.Jobs;

public interface IJobService
{
    Task<JobResponse> CreateJobAsync(CreateJobRequest request, string recruiterId);
}
