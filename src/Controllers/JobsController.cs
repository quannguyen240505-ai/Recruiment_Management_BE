using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Recruitment.Api.Dtos.Job;
using Recruitment.Api.Entities;
using Recruitment.Api.Services.Jobs;
using System.Security.Claims;

namespace Recruitment.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private readonly IJobService _jobService;

    public JobsController(IJobService jobService)
    {
        _jobService = jobService;
    }

    [HttpPost]
    [Authorize(Policy = "RequireRecruiterRole")]
    public async Task<IActionResult> CreateJob([FromBody] CreateJobRequest request)
    {
        var recruiterId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(recruiterId)) return Unauthorized();

        try
        {
            var result = await _jobService.CreateJobAsync(request, recruiterId);
            return StatusCode(201, result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Lỗi Validation", Detail = ex.Message, Status = 400 });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new ProblemDetails { Title = "Lỗi Phân quyền", Detail = ex.Message, Status = 403 });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(403, new ProblemDetails { Title = "Lỗi Tài khoản", Detail = ex.Message, Status = 403 });
        }
    }

    [HttpGet("{jobId}")]
    [AllowAnonymous]
    public async Task<ActionResult<JobDetailResponse>> GetJobDetail(string jobId)
    {
        var job = await _jobService.GetJobDetailAsync(jobId);

        if (job == null)
        {
            return NotFound(new
            {
                code = "JOB_NOT_FOUND",
                message = "Không tìm thấy hoặc không thể xem công việc này."
            });
        }

        return Ok(job);
    }
    [HttpGet("/api/recruiter/jobs")]
    [Authorize(Policy = "RequireRecruiterRole")]
    public async Task<IActionResult> GetMyJobs(
    [FromQuery] JobStatus? status,
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 10)
    {
        var recruiterId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(recruiterId)) return Unauthorized();

        try
        {
            var result = await _jobService.GetMyJobsAsync(recruiterId, status, page, pageSize);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            // Trả về 400 cho tham số không hợp lệ
            return BadRequest(new ProblemDetails { Title = "Lỗi tham số", Detail = ex.Message, Status = 400 });
        }
        catch (UnauthorizedAccessException ex)
        {
            // Trả về 403 khi tài khoản không còn quyền
            return StatusCode(403, new ProblemDetails { Title = "Lỗi Phân quyền", Detail = ex.Message, Status = 403 });
        }
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<SearchJobsResponse>> SearchJobs(
        [FromQuery] SearchJobsRequest request)
    {
        try
        {
            var result = await _jobService.SearchJobsAsync(request);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Tham số tìm kiếm không hợp lệ",
                Detail = ex.Message,
                Status = 400
            });
        }
    }

    // SCRUM-190: Preload Edit Job (Recruiter owner only).
    [HttpGet("/api/recruiter/jobs/{jobId}")]
    [Authorize(Policy = "RequireRecruiterRole")]
    public async Task<IActionResult> GetRecruiterJobDetail(string jobId)
    {
        var recruiterId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(recruiterId)) return Unauthorized();
        try
        {
            var result = await _jobService.GetRecruiterJobDetailAsync(jobId, recruiterId);
            if (result == null) return NotFound(new { code = "JOB_NOT_FOUND" });
            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(403, new ProblemDetails
            {
                Title = "KhÃ´ng cÃ³ quyá»n xem Job", Status = 403
            });
        }
    }

    [HttpPut("/api/recruiter/jobs/{jobId}")]
    [Authorize(Policy = "RequireRecruiterRole")]
    public async Task<IActionResult> UpdateJob(
        string jobId,
        [FromBody] UpdateJobRequest request)
    {
        var recruiterId = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(recruiterId))
            return Unauthorized();

        try
        {
            var result = await _jobService.UpdateJobAsync(
                jobId,
                request,
                recruiterId);

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Dữ liệu không hợp lệ",
                Detail = ex.Message,
                Status = 400
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Không tìm thấy Job",
                Detail = ex.Message,
                Status = 404
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new ProblemDetails
            {
                Title = "Không có quyền chỉnh sửa",
                Detail = ex.Message,
                Status = 403
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Không thể cập nhật Job",
                Detail = ex.Message,
                Status = 409
            });
        }
    }
}
