using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Recruitment.Api.Controllers;
using Recruitment.Api.Dtos;
using Recruitment.Api.Dtos.Job;
using Recruitment.Api.Entities;
using Recruitment.Api.Services.Jobs;

namespace Recruitment.Api.Tests;

// Unit tests thuần: dùng FakeJobService, không chạm database.
public class UpdateJobControllerTests
{
    private static UpdateJobRequest Request() => new()
    {
        Title = "Updated title",
        Description = "Updated description",
        Requirements = "C#",
        Location = "Da Nang",
        JobType = JobType.FULL_TIME,
        Experience = "3 years",
        NumberOfPositions = 2,
        Deadline = DateTime.UtcNow.AddDays(30),
        CategoryId = "CAT-01",
        SkillIds = new List<string> { "SKILL-01" }
    };

    private static JobsController Controller(FakeJobService service, string? recruiterId = "REC-01")
    {
        var http = new DefaultHttpContext();
        if (!string.IsNullOrWhiteSpace(recruiterId))
        {
            http.User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, recruiterId) },
                authenticationType: "TestAuthentication"));
        }

        return new JobsController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
    }

    [Fact]
    public async Task UpdateJob_NoUserId_Returns401_WithoutCallingService()
    {
        var service = FakeJobService.Succeed();
        var result = await Controller(service, recruiterId: null)
            .UpdateJob("JOB-01", Request());

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Equal(0, service.CallCount);
    }

    [Fact]
    public async Task UpdateJob_ServiceSucceeds_Returns200_AndForwardsArguments()
    {
        var response = new JobResponse
        {
            JobId = "JOB-01", Title = "Updated title", Status = "OPEN"
        };
        var service = FakeJobService.Succeed(response);
        var request = Request();

        var result = await Controller(service, "REC-01")
            .UpdateJob("JOB-01", request);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);
        Assert.Same(response, ok.Value);
        Assert.Equal(1, service.CallCount);
        Assert.Equal("JOB-01", service.LastJobId);
        Assert.Equal("REC-01", service.LastRecruiterId);
        Assert.Same(request, service.LastRequest);
    }

    [Fact]
    public async Task UpdateJob_ArgumentException_Returns400()
    {
        var result = await Controller(FakeJobService.Fail(
            new ArgumentException("SalaryMax không hợp lệ")))
            .UpdateJob("JOB-01", Request());

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal(400, problem.Status);
        Assert.Contains("SalaryMax", problem.Detail);
    }

    [Fact]
    public async Task UpdateJob_KeyNotFoundException_Returns404()
    {
        var result = await Controller(FakeJobService.Fail(
            new KeyNotFoundException("Không tìm thấy Job")))
            .UpdateJob("MISSING", Request());

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(404, notFound.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(notFound.Value);
        Assert.Equal(404, problem.Status);
    }

    [Fact]
    public async Task UpdateJob_UnauthorizedAccessException_Returns403()
    {
        var result = await Controller(FakeJobService.Fail(
            new UnauthorizedAccessException("Không có quyền sửa Job")))
            .UpdateJob("JOB-01", Request());

        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, forbidden.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(forbidden.Value);
        Assert.Equal(403, problem.Status);
    }

    [Fact]
    public async Task UpdateJob_InvalidOperationException_Returns409()
    {
        var result = await Controller(FakeJobService.Fail(
            new InvalidOperationException("Job đã đóng")))
            .UpdateJob("JOB-01", Request());

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(409, conflict.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(409, problem.Status);
    }

    private sealed class FakeJobService : IJobService
    {
        private readonly Func<string, UpdateJobRequest, string, Task<JobResponse>> _update;

        private FakeJobService(Func<string, UpdateJobRequest, string, Task<JobResponse>> update)
            => _update = update;

        public int CallCount { get; private set; }
        public string? LastJobId { get; private set; }
        public string? LastRecruiterId { get; private set; }
        public UpdateJobRequest? LastRequest { get; private set; }

        public static FakeJobService Succeed(JobResponse? response = null) =>
            new((_, _, _) => Task.FromResult(response ?? new JobResponse()));

        public static FakeJobService Fail(Exception exception) =>
            new((_, _, _) => Task.FromException<JobResponse>(exception));

        public Task<JobResponse> UpdateJobAsync(
            string jobId, UpdateJobRequest request, string recruiterId)
        {
            CallCount++;
            LastJobId = jobId;
            LastRequest = request;
            LastRecruiterId = recruiterId;
            return _update(jobId, request, recruiterId);
        }

        public Task<JobResponse> CreateJobAsync(CreateJobRequest request, string recruiterId)
            => throw new NotSupportedException();

        public Task<JobDetailResponse?> GetJobDetailAsync(string jobId)
            => throw new NotSupportedException();

        public Task<PagedResult<JobSummaryResponse>> GetMyJobsAsync(
            string recruiterId, JobStatus? status, int page, int pageSize)
            => throw new NotSupportedException();

        public Task<SearchJobsResponse> SearchJobsAsync(SearchJobsRequest request)
            => throw new NotSupportedException();
    }
}
