
using Google.Apis.Auth;

namespace Recruitment.Api.Services.Auth;

public interface IGoogleTokenValidator
{
    Task<GoogleJsonWebSignature.Payload> ValidateAsync(string idToken);
}

public sealed class GoogleTokenValidator : IGoogleTokenValidator
{
    private readonly IConfiguration _configuration;

    public GoogleTokenValidator(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<GoogleJsonWebSignature.Payload> ValidateAsync(
        string idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken))
            throw new UnauthorizedAccessException(
                "Google ID Token không hợp lệ.");

        var clientId = _configuration["Google:ClientId"];

        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException(
                "Google Client ID chưa được cấu hình.");

        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { clientId }
            };

            var payload = await GoogleJsonWebSignature.ValidateAsync(
                idToken, settings);

            if (string.IsNullOrWhiteSpace(payload.Subject) ||
                string.IsNullOrWhiteSpace(payload.Email) ||
                !payload.EmailVerified)
            {
                throw new UnauthorizedAccessException(
                    "Không thể xác minh tài khoản Google.");
            }

            return payload;
        }
        catch (InvalidJwtException ex)
        {
            throw new UnauthorizedAccessException(
                "Google ID Token không hợp lệ hoặc đã hết hạn.", ex);
        }
    }
}
