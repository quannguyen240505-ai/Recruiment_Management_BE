using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Recruitment.Api.Dtos;
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
}
