
using System.ComponentModel.DataAnnotations;
using Recruitment.Api.Dtos.Auth;
using Xunit;

namespace Recruitment.Api.Tests;

public class ChangePasswordRequestTests
{
    [Theory]
    [InlineData("Test@123456", "short", "short")]
    [InlineData("Test@123456", "password@123", "password@123")]
    [InlineData("Test@123456", "PASSWORD@123", "PASSWORD@123")]
    [InlineData("Test@123456", "Password@abc", "Password@abc")]
    [InlineData("Test@123456", "Password123", "Password123")]
    [InlineData("Test@123456", "New@123456", "Other@123456")]
    [InlineData("", "New@123456", "New@123456")]
    public void InvalidRequest_ShouldFailValidation(
        string currentPassword,
        string newPassword,
        string confirmPassword)
    {
        var request = new ChangePasswordRequest
        {
            CurrentPassword = currentPassword,
            NewPassword = newPassword,
            ConfirmPassword = confirmPassword
        };

        var results = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            results,
            validateAllProperties: true);

        Assert.False(isValid);
        Assert.NotEmpty(results);
    }

    [Fact]
    public void ValidRequest_ShouldPassValidation()
    {
        var request = new ChangePasswordRequest
        {
            CurrentPassword = "Test@123456",
            NewPassword = "New@123456",
            ConfirmPassword = "New@123456"
        };

        var results = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            results,
            validateAllProperties: true);

        Assert.True(isValid);
        Assert.Empty(results);
    }
}
