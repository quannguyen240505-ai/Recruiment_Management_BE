
namespace Recruitment.Api.Services.Auth;

public class RegistrationConflictException : Exception
{
    public RegistrationConflictException(string message)
        : base(message)
    {
    }
}
