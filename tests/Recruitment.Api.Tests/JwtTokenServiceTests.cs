
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Recruitment.Api.Entities;
using Recruitment.Api.Services.Auth;
using Xunit;

namespace Recruitment.Api.Tests;

public class JwtTokenServiceTests
{
    [Fact]
    public void CreateToken_ValidUser_ShouldReturnValidJwtWithRole()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "0123456789abcdef0123456789abcdef",
                ["Jwt:Issuer"] = "RecruitmentApi",
                ["Jwt:Audience"] = "RecruitmentClient"
            })
            .Build();

        var user = new User
        {
            UserId = "user-123",
            Username = "testlogin01",
            RoleId = "CANDIDATE"
        };

        var jwtService = new JwtTokenService(configuration);
        var (accessToken, expiresAt) = jwtService.CreateToken(user);

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(configuration["Jwt:Key"]!)),
            ValidateIssuer = true,
            ValidIssuer = configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        var handler = new JwtSecurityTokenHandler();

        var principal = handler.ValidateToken(
            accessToken,
            validationParameters,
            out var validatedToken);

        var jwt = Assert.IsType<JwtSecurityToken>(validatedToken);

        Assert.Equal(SecurityAlgorithms.HmacSha256, jwt.Header.Alg);
        Assert.True(principal.IsInRole("CANDIDATE"));
        Assert.True(expiresAt > DateTime.UtcNow);
    }
}
