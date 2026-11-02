
using System.ComponentModel.DataAnnotations;
using Recruitment.Api.Dtos.Auth;
using Xunit;

namespace Recruitment.Api.Tests;

public class RegisterValidationTests
{
    private static List<ValidationResult> Validate(RegisterRequest request)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(request);

        Validator.TryValidateObject(
            request, context, results, validateAllProperties: true);

        return results;
    }

    private static RegisterRequest ValidRequest() => new()
    {
        FullName = "Nguyen Van A",
        Username = "nguyenvana",
        Email = "vana@gmail.com",
        PhoneNumber = "0912345678",
        Password = "Test@123456",
        ConfirmPassword = "Test@123456"
    };

    [Fact]
    public void Register_ValidData_ShouldPass()
    {
        var request = ValidRequest();

        var errors = Validate(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void Register_EmptyFullName_ShouldFail()
    {
        var request = ValidRequest();
        request.FullName = "   ";

        var errors = Validate(request);

        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Register_EmptyUsername_ShouldFail()
    {
        var request = ValidRequest();
        request.Username = "   ";

        var errors = Validate(request);

        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Register_InvalidEmail_ShouldFail()
    {
        var request = ValidRequest();
        request.Email = "email-sai";

        var errors = Validate(request);

        Assert.NotEmpty(errors);
    }

    [Theory]
    [InlineData("123456")]
    [InlineData("abcdefgh")]
    [InlineData("ABCDEFGH")]
    [InlineData("Abcdef12")]
    public void Register_WeakPassword_ShouldFail(string password)
    {
        var request = ValidRequest();
        request.Password = password;
        request.ConfirmPassword = password;

        var errors = Validate(request);

        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Register_PasswordMismatch_ShouldFail()
    {
        var request = ValidRequest();
        request.ConfirmPassword = "Wrong@123456";

        var errors = Validate(request);

        Assert.NotEmpty(errors);
    }
}
