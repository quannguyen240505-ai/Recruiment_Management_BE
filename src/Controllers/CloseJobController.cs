
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Recruitment.Api.Services.Jobs;
using System.Security.Claims;

namespace Recruitment.Api.Controllers;

[ApiController]
[Route("api/recruiter/jobs")]
[Authorize(Policy = "RequireRecruiterRole")]
public class CloseJobController : ControllerBase
{
    private readonly CloseJobService _closeJobService;

    public CloseJobController(CloseJobService closeJobService)
    {
        _closeJobService = closeJobService;
    }

    [HttpPatch("{jobId}/close")]
    public async Task<IActionResult> CloseJob(string jobId)
    {
        var recruiterId = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(recruiterId))
            return Unauthorized();

        try
        {
            var result = await _closeJobService
                .CloseJobAsync(recruiterId, jobId);

            if (result == null)
            {
                return NotFound(new ProblemDetails
                {
                    Title = "Không tìm thấy Job",
                    Detail = "Job không tồn tại hoặc không thuộc quyền quản lý.",
                    Status = 404
                });
            }

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
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new ProblemDetails
            {
                Title = "Không có quyền truy cập",
                Detail = ex.Message,
                Status = 403
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Không thể đóng Job",
                Detail = ex.Message,
                Status = 409
            });
        }
    }
}
