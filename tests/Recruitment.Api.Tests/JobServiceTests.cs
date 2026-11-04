using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Recruitment.Api.Data;
using Recruitment.Api.Dtos;
using Recruitment.Api.Entities;
using Recruitment.Api.Services.Jobs;

namespace Recruitment.Api.Tests;

public class JobServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly JobService _jobService;

    public JobServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _jobService = new JobService(_db);

        // Seed dữ liệu cơ bản (Company, Category, Skill, Recruiter)
        SeedData();
    }

    private void SeedData()
    {
        _db.Companies.Add(new Company { CompanyId = "COMP-01", Name = "Test Company" });
        _db.Categories.Add(new Category { CategoryId = "CAT-01", Name = "IT" });
        _db.Skills.Add(new Skill { SkillId = "SKILL-01", Name = "C#" });
        _db.Users.Add(new User
        {
            UserId = "REC-01",
            Username = "recruiter",
            Email = "rec@test.com",
            FullName = "Recruiter",
            RoleId = "RECRUITER",
            CompanyId = "COMP-01",
            AccountStatus = AccountStatus.ACTIVE,
            CreatedAtUtc = DateTime.UtcNow
        });
        _db.SaveChanges();
    }

    private CreateJobRequest GetValidRequest() => new()
    {
        Title = "Senior Dev",
        Description = "Desc",
        Requirements = "Req",
        Location = "HCM",
        JobType = JobType.FULL_TIME,
        Experience = "3 years",
        NumberOfPositions = 2,
        SalaryMin = 1000,
        SalaryMax = 2000,
        Deadline = DateTime.UtcNow.AddDays(30),
        CategoryId = "CAT-01",
        SkillIds = new List<string> { "SKILL-01" }
    };

    [Fact]
    public async Task CreateJob_ValidRequest_ReturnsJob()
    {
        var result = await _jobService.CreateJobAsync(GetValidRequest(), "REC-01");
        Assert.NotNull(result);
        Assert.Equal("Senior Dev", result.Title);
        Assert.Equal("OPEN", result.Status);
    }

    [Fact]
    public async Task CreateJob_DeadlineInPast_Throws()
    {
        var req = GetValidRequest();
        req.Deadline = DateTime.UtcNow.AddDays(-1);
        await Assert.ThrowsAsync<ArgumentException>(() => _jobService.CreateJobAsync(req, "REC-01"));
    }

    [Fact]
    public async Task CreateJob_SalaryMaxLessThanMin_Throws()
    {
        var req = GetValidRequest();
        req.SalaryMin = 2000; req.SalaryMax = 1000;
        await Assert.ThrowsAsync<ArgumentException>(() => _jobService.CreateJobAsync(req, "REC-01"));
    }

    [Fact]
    public async Task CreateJob_UserNotRecruiter_Throws()
    {
        // Thêm một Candidate
        _db.Users.Add(new User { UserId = "CAN-01", Username = "cand", Email = "cand@test.com", FullName = "Cand", RoleId = "CANDIDATE", AccountStatus = AccountStatus.ACTIVE, CreatedAtUtc = DateTime.UtcNow });
        _db.SaveChanges();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _jobService.CreateJobAsync(GetValidRequest(), "CAN-01"));
    }

    [Fact]
    public async Task CreateJob_RecruiterWithoutCompany_Throws()
    {
        _db.Users.Add(new User { UserId = "REC-02", Username = "rec2", Email = "rec2@test.com", FullName = "Rec2", RoleId = "RECRUITER", CompanyId = null, AccountStatus = AccountStatus.ACTIVE, CreatedAtUtc = DateTime.UtcNow });
        _db.SaveChanges();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _jobService.CreateJobAsync(GetValidRequest(), "REC-02"));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
