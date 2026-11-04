
using Recruitment.Api.Dtos.Auth;

namespace Recruitment.Api.Services.Auth;

public interface IAuthService
{
    Task<RegisterResponse> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default);

    Task<LoginResponse> LoginAsync(
       LoginRequest request,
       CancellationToken cancellationToken = default);
}
