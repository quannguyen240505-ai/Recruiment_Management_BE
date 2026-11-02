using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Recruitment.Api.Data;
using Recruitment.Api.Entities;

namespace Recruitment.Api.Tests;

public class AuthSchemaTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;

    public AuthSchemaTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
    }

    [Fact]
    public void Roles_AreSeeded()
    {
        var ids = _db.Roles.Select(r => r.RoleId).OrderBy(id => id).ToList();
        Assert.Equal(new[] { "ADMIN", "CANDIDATE", "RECRUITER" }, ids);
    }

    [Fact]
    public void User_CanBeSavedAndRead()
    {
        _db.Users.Add(new User
        {
            UserId = "USER-001",
            Email = "a@test.com",
            Username = "usera",
            PasswordHash = "hash",
            FullName = "User A",
            RoleId = "CANDIDATE",
            CreatedAtUtc = DateTime.UtcNow
        });
        _db.SaveChanges();

        var user = _db.Users.Include(u => u.Role).Single(u => u.Email == "a@test.com");
        Assert.Equal("CANDIDATE", user.Role.RoleId);
        Assert.Equal("Candidate", user.Role.RoleName);
    }

    [Fact]
    public void DuplicateEmail_Throws()
    {
        User Make(string id, string email) => new()
        {
            UserId = id,
            Email = email,
            Username = "user" + id,
            PasswordHash = "h",
            FullName = "X",
            RoleId = "CANDIDATE",
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Users.Add(Make("USER-1", "dup@test.com"));
        _db.SaveChanges();

        _db.Users.Add(Make("USER-2", "dup@test.com"));

        // Kỳ vọng ném ra lỗi vì trùng Unique Email
        Assert.Throws<DbUpdateException>(() => _db.SaveChanges());
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
