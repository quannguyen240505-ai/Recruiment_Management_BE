using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Recruitment.Api.Services.Cvs;

namespace Recruitment.Api.Controllers;

[ApiController]
[Route("api/cvs")]
[Authorize(Policy = "RequireCandidateRole")]
public sealed class CvsController : ControllerBase
{
    private readonly ICvService _service;
    public CvsController(ICvService service) => _service = service;

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();
        try
        {
            var cv = await _service.UploadAsync(userId, file, ct);
            return CreatedAtAction(nameof(Download), new { id = cv.CvId }, cv);
        }
        catch (CvValidationException ex) { return BadRequest(CvProblem(400, ex.Message)); }
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();
        return Ok(await _service.ListAsync(userId, ct));
    }

    [HttpGet("{id}/download")]
    public async Task<IActionResult> Download(string id, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();
        var cv = await _service.GetDownloadAsync(userId, id, ct);
        if (cv is null) return NotFound();
        // PhysicalFile is served only after the user/ownership check.
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(cv.Path, cv.ContentType, cv.OriginalFileName, enableRangeProcessing: false);
    }

    [HttpPut("{id}")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Replace(string id, IFormFile file, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();
        try
        {
            var replacement = await _service.ReplaceAsync(userId, id, file, ct);
            return replacement is null ? NotFound() : Ok(replacement);
        }
        catch (CvValidationException ex) { return BadRequest(CvProblem(400, ex.Message)); }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();
        return await _service.DeleteAsync(userId, id, ct) ? NoContent() : NotFound();
    }

    private static ProblemDetails CvProblem(int status, string detail) => new()
    {
        Title = "CV validation error",
        Detail = detail,
        Status = status
    };
}
