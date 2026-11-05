
using System.ComponentModel.DataAnnotations;

namespace Recruitment.Api.Dtos.Auth;

public class GoogleLoginRequest
{
    [Required(ErrorMessage = "Google ID Token không được để trống.")]
    public string IdToken { get; set; } = string.Empty;
}
