
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

            ValidateGoogleClaims(payload);
            ValidateTokenMetadata(payload, clientId, DateTimeOffset.UtcNow);
            return payload;
        }
        catch (InvalidJwtException ex)
        {
            throw new UnauthorizedAccessException(
                "Google ID Token không hợp lệ hoặc đã hết hạn.", ex);
        }
    }

    public static void ValidateGoogleClaims(
        GoogleJsonWebSignature.Payload payload)
    {
        if (payload == null ||
            string.IsNullOrWhiteSpace(payload.Subject) ||
            string.IsNullOrWhiteSpace(payload.Email) ||
            !payload.EmailVerified)
        {
            throw new UnauthorizedAccessException(
                "Không thể xác minh tài khoản Google.");
        }
    }

    public static void ValidateTokenMetadata(
        GoogleJsonWebSignature.Payload payload,
        string expectedAudience,
        DateTimeOffset now)
    {
        if (!payload.AudienceAsList.Contains(
                expectedAudience, StringComparer.Ordinal))
        {
            throw new UnauthorizedAccessException(
                "Google ID Token sai Audience.");
        }

        if (!payload.ExpirationTimeSeconds.HasValue ||
            payload.ExpirationTimeSeconds.Value <= now.ToUnixTimeSeconds())
        {
            throw new UnauthorizedAccessException(
                "Google ID Token đã hết hạn.");
        }
    }

}
