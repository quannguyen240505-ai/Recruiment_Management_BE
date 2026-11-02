
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Recruitment.Api.Controllers;
using Recruitment.Api.Dtos.Auth;
using Recruitment.Api.Services.Auth;
using Xunit;

namespace Recruitment.Api.Tests;

public class AuthControllerTests
{
    [Fact]
    public async Task Register_Success_ShouldReturn201()
    {
        var expected = new RegisterResponse
        {
            UserId = "user-1",
            Username = "nguyenvana",
            Email = "vana@gmail.com",
            FullName = "Nguyen Van A",
            Role = "CANDIDATE"
        };

        var controller = new AuthController(
            new FakeAuthService(expected));

        var request = new RegisterRequest
        {
            FullName = "Nguyen Van A",
            Username = "nguyenvana",
            Email = "vana@gmail.com",
            PhoneNumber = "0912345678",
            Password = "Test@123456",
            ConfirmPassword = "Test@123456"
        };

        var result = await controller.Register(
            request, CancellationToken.None);

        var response = Assert.IsType<ObjectResult>(result.Result);

        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
        Assert.Same(expected, response.Value);
    }

    private sealed class FakeAuthService : IAuthService
    {
        private readonly RegisterResponse _response;

        public FakeAuthService(RegisterResponse response)
        {
            _response = response;
        }

        public Task<RegisterResponse> RegisterAsync(
            RegisterRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_response);
        }
    }
}
