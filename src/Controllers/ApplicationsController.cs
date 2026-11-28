
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Recruitment.Api.Services.Applications;
using System.Security.Claims;

namespace Recruitment.Api.Controllers;

[ApiController]
[Route("api/candidate/applications")]
[Authorize(Policy = "RequireCandidateRole")]
public class ApplicationsController : ControllerBase
{
    private readonly ApplicationService _applicationService;

    public ApplicationsController(
        ApplicationService applicationService)
    {
        _applicationService = applicationService;
    }

    // SCRUM-181: Xem danh sách hồ sơ của Candidate
    [HttpGet]
    public async Task<IActionResult> GetMyApplications(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        var candidateId = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(candidateId))
            return Unauthorized();

        try
        {
            var result = await _applicationService
                .GetMyApplicationsAsync(
                    candidateId, page, pageSize);

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Tham số không hợp lệ",
                Detail = ex.Message,
                Status = 400
            });
        }
    }

    // SCRUM-181: Xem chi tiết một hồ sơ ứng tuyển
    [HttpGet("{applicationId}")]
    public async Task<IActionResult> GetMyApplicationDetail(
        string applicationId)
    {
        var candidateId = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(candidateId))
            return Unauthorized();

        var result = await _applicationService
            .GetMyApplicationDetailAsync(
                candidateId, applicationId);

        if (result == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Không tìm thấy hồ sơ",
                Detail = "Application không tồn tại hoặc không thuộc quyền truy cập.",
                Status = 404
            });
        }

        return Ok(result);
    }
}
