using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Recruitment.Api.Dtos.Auth;
using Recruitment.Api.Services.Auth;

namespace Recruitment.Api.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
[EnableRateLimiting("password-reset")]
public sealed class PasswordResetController(IPasswordResetService service) : ControllerBase
{
    public const string AcceptedMessage = "Nếu tài khoản phù hợp, hướng dẫn khôi phục sẽ được gửi đến email của bạn.";

    [HttpPost("forgot-password")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await service.RequestAsync(request, cancellationToken);
            return Accepted(new { message = AcceptedMessage });
        }
        catch (ValidationException) { return InvalidRequest(); }
        catch (PasswordResetUnavailableException)
        {
            return Problem(statusCode: 503, title: "Chức năng khôi phục tạm thời chưa sẵn sàng.");
        }
    }

    [HttpPost("reset-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await service.ResetAsync(request, cancellationToken);
            return NoContent();
        }
        catch (InvalidPasswordResetException)
        {
            return Problem(statusCode: 400, title: "Mã khôi phục không hợp lệ hoặc không còn hiệu lực.");
        }
        catch (ValidationException) { return InvalidRequest(); }
    }

    private ObjectResult InvalidRequest() => Problem(statusCode: 400,
        title: "Dữ liệu không hợp lệ. Kiểm tra thông tin và chính sách mật khẩu.");
}
