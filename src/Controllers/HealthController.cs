using System.Reflection;
using Microsoft.AspNetCore.Mvc;

namespace Recruitment.Api.Controllers;

[ApiController]
[Route("api/v1/health")]
public class HealthController(IWebHostEnvironment env) : ControllerBase
{
    /// <summary>Kiểm tra backend đang chạy. Không cần đăng nhập, không trả thông tin nhạy cảm.</summary>
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        status = "Healthy",
        service = "Recruitment.Api",
        version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
        environment = env.EnvironmentName,
        timestamp = DateTime.UtcNow
    });
}
