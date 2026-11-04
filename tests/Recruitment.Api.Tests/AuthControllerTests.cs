
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Recruitment.Api.Controllers;
using Recruitment.Api.Dtos.Auth;
using Recruitment.Api.Services.Auth;
using Xunit;

namespace Recruitment.Api.Tests;

public class AuthControllerTests
{
    // TEST 1: Đăng ký thành công
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

    // TEST 2: Đăng ký trùng tài khoản
    [Theory]
    [InlineData("Email đã tồn tại.")]
    [InlineData("Tên đăng nhập đã tồn tại.")]
    public async Task Register_DuplicateAccount_ShouldReturn409(
        string message)
    {
        var controller = new AuthController(
            new FakeConflictAuthService(message));

        var result = await controller.Register(
            new RegisterRequest(), CancellationToken.None);

        var response = Assert.IsType<ConflictObjectResult>(result.Result);

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);

        var problem = Assert.IsType<ProblemDetails>(response.Value);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
        Assert.Equal(message, problem.Detail);
    }

    // TEST 3: Logout thành công trả HTTP 204
    [Fact]
    public void Logout_ShouldReturn204NoContent()
    {
        var controller = new AuthController(
            new FakeAuthService(new RegisterResponse()));

        var result = controller.Logout();

        Assert.IsType<NoContentResult>(result);
    }

    // TEST 4: Logout bắt buộc xác thực JWT
    [Fact]
    public void Logout_ShouldRequireAuthorization()
    {
        var method = typeof(AuthController)
            .GetMethod(nameof(AuthController.Logout));

        Assert.NotNull(method);

        Assert.True(Attribute.IsDefined(
            method, typeof(AuthorizeAttribute)));

        Assert.False(Attribute.IsDefined(
            method, typeof(AllowAnonymousAttribute)));

        var httpPost = Assert.Single(
            method.GetCustomAttributes(
                typeof(HttpPostAttribute), false)
            .Cast<HttpPostAttribute>());

        Assert.Equal("logout", httpPost.Template);
    }

    private sealed class FakeAuthService : IAuthService
    {
        private readonly RegisterResponse _response;

        public Task<LoginResponse> LoginAsync(
            LoginRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }


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

    private sealed class FakeConflictAuthService : IAuthService
    {
        private readonly string _message;

        public Task<LoginResponse> LoginAsync(
            LoginRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public FakeConflictAuthService(string message)
        {
            _message = message;
        }

        public Task<RegisterResponse> RegisterAsync(
            RegisterRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<RegisterResponse>(
                new RegistrationConflictException(_message));
        }
    }
}
