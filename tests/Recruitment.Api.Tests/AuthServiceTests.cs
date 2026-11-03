
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Recruitment.Api.Data;
using Recruitment.Api.Dtos.Auth;
using Recruitment.Api.Entities;
using Recruitment.Api.Services.Auth;
using Xunit;

namespace Recruitment.Api.Tests;

public class AuthServiceTests
{
    [Fact]
    public async Task Register_ShouldHashPasswordAndAssignCandidateRole()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var hasher = new PasswordHasher<User>();
        var service = new AuthService(db, hasher);

        var request = new RegisterRequest
        {
            FullName = "Nguyen Van A",
            Username = "testuser",
            Email = "testuser@example.com",
            PhoneNumber = "0912345678",
            Password = "Test@123456",
            ConfirmPassword = "Test@123456"
        };

        var response = await service.RegisterAsync(request);

        var user = await db.Users.SingleAsync(
            u => u.UserId == response.UserId);

        Assert.NotNull(user.PasswordHash);
        Assert.NotEqual(request.Password, user.PasswordHash);
        Assert.Equal("CANDIDATE", user.RoleId);

        var result = hasher.VerifyHashedPassword(
            user, user.PasswordHash!, request.Password);

        Assert.Equal(PasswordVerificationResult.Success, result);
    }
}
