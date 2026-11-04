
using Microsoft.AspNetCore.Mvc;
using Recruitment.Api.Controllers;
using Recruitment.Api.Dtos.Job;
using Recruitment.Api.Services.Jobs;
using System.Text.Json;

namespace Recruitment.Api.Tests;

public class JobsControllerTests
{
    [Fact]
    public async Task GetJobDetail_ValidJob_Returns200()
    {
        var job = new JobDetailResponse
        {
            JobId = "JOB-01",
            Title = "Backend Developer",
            Status = "OPEN"
        };

        var controller = new JobsController(
            new FakeJobService(job));

        var result = await controller.GetJobDetail("JOB-01");

        var response = Assert.IsType<OkObjectResult>(result.Result);

        Assert.Equal(200, response.StatusCode);
        Assert.Same(job, response.Value);
    }

    [Fact]
    public async Task GetJobDetail_NotFound_Returns404()
    {
        var controller = new JobsController(
            new FakeJobService(null));

        var result = await controller.GetJobDetail("JOB-NOT-EXIST");

        var response = Assert.IsType<NotFoundObjectResult>(
            result.Result);

        Assert.Equal(404, response.StatusCode);

        var json = JsonSerializer.Serialize(response.Value);
        Assert.Contains("JOB_NOT_FOUND", json);
    }

    private sealed class FakeJobService : IJobService
    {
        private readonly JobDetailResponse? _job;

        public FakeJobService(JobDetailResponse? job)
        {
            _job = job;
        }

        public Task<JobDetailResponse?> GetJobDetailAsync(string jobId)
        {
            return Task.FromResult(_job);
        }

        public Task<JobResponse> CreateJobAsync(
            CreateJobRequest request,
            string recruiterId)
        {
            throw new NotSupportedException();
        }
    }
}
