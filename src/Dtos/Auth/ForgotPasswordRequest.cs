using System.ComponentModel.DataAnnotations;

namespace Recruitment.Api.Dtos.Auth;

public sealed class ForgotPasswordRequest
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;
}
