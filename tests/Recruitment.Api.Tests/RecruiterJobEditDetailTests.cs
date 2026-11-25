using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Recruitment.Api.Data;
using Recruitment.Api.Entities;
using Recruitment.Api.Services.Jobs;

namespace Recruitment.Api.Tests;

// Integration: kiểm thử query owner-only trên SQLite in-memory.
public sealed class RecruiterJobEditDetailTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly AppDbContext _db;
    private readonly JobService _service;

    public RecruiterJobEditDetailTests()
    {
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _db.Companies.Add(new Company { CompanyId = "C-1", Name = "Company" });
        _db.Categories.Add(new Category { CategoryId = "CAT-IT", Name = "IT" });
        _db.Skills.Add(new Skill { SkillId = "SK-1", Name = "C#" });
        _db.Users.Add(new User { UserId = "REC-A", Username = "rec-a", Email = "rec-a@test.com", FullName = "A", RoleId = "RECRUITER", CompanyId = "C-1", AccountStatus = AccountStatus.ACTIVE });
        _db.Users.Add(new User { UserId = "REC-B", Username = "rec-b", Email = "rec-b@test.com", FullName = "B", RoleId = "RECRUITER", CompanyId = "C-1", AccountStatus = AccountStatus.ACTIVE });
        _db.Users.Add(new User { UserId = "CAN-A", Username = "can-a", Email = "can-a@test.com", FullName = "Candidate", RoleId = "CANDIDATE", AccountStatus = AccountStatus.ACTIVE });
        _db.Jobs.Add(new Job {
            JobId = "JOB-A", Title = "C# Dev", Description = "Description", Requirements = "C#",
            Location = "Da Nang", JobType = JobType.FULL_TIME, Experience = "2 years",
            NumberOfPositions = 2, SalaryMin = 100, SalaryMax = 200, Deadline = DateTime.UtcNow.AddDays(30),
            Status = JobStatus.OPEN, CategoryId = "CAT-IT", CompanyId = "C-1", RecruiterId = "REC-A"
        });
        _db.JobSkills.Add(new JobSkill { JobId = "JOB-A", SkillId = "SK-1" });
        _db.SaveChanges();
        _service = new JobService(_db);
    }

    [Fact]
    public async Task Owner_CanReadJobDetail()
    {
        var detail = await _service.GetRecruiterJobDetailAsync("JOB-A", "REC-A");
        Assert.NotNull(detail);
        Assert.Equal("JOB-A", detail.JobId);
        Assert.Equal("C# Dev", detail.Title);
    }

    [Fact]
    public async Task Owner_DetailContainsCategoryId()
    {
        var detail = await _service.GetRecruiterJobDetailAsync("JOB-A", "REC-A");
        Assert.Equal("CAT-IT", detail!.CategoryId);
    }

    [Fact]
    public async Task Owner_DetailContainsSkillIds()
    {
        var detail = await _service.GetRecruiterJobDetailAsync("JOB-A", "REC-A");
        Assert.Contains(detail!.Skills, skill => skill.SkillId == "SK-1");
    }

    [Fact]
    public async Task Owner_DetailContainsEditableFields()
    {
        var detail = await _service.GetRecruiterJobDetailAsync("JOB-A", "REC-A");
        Assert.Equal("Da Nang", detail!.Location);
        Assert.Equal(2, detail.NumberOfPositions);
        Assert.Equal(100m, detail.SalaryMin);
        Assert.Equal("FULL_TIME", detail.JobType);
    }

    [Fact]
    public async Task Owner_DetailContainsCompany()
    {
        var detail = await _service.GetRecruiterJobDetailAsync("JOB-A", "REC-A");
        Assert.Equal("Company", detail!.Company.Name);
        Assert.Equal("C-1", detail.Company.CompanyId);
    }

    [Fact]
    public async Task OtherRecruiter_SameCompany_CannotReadJob()
    {
        var detail = await _service.GetRecruiterJobDetailAsync("JOB-A", "REC-B");
        Assert.Null(detail);
    }

    [Fact]
    public async Task Candidate_CannotReadJob()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.GetRecruiterJobDetailAsync("JOB-A", "CAN-A"));
    }

    [Fact]
    public async Task UnknownUser_CannotReadJob()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.GetRecruiterJobDetailAsync("JOB-A", "NOT-FOUND"));
    }

    [Fact]
    public async Task LockedRecruiter_CannotReadJob()
    {
        var recruiter = await _db.Users.FindAsync("REC-A");
        recruiter!.AccountStatus = AccountStatus.LOCKED;
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.GetRecruiterJobDetailAsync("JOB-A", "REC-A"));
    }

    [Fact]
    public async Task DeletedRecruiter_CannotReadJob()
    {
        var recruiter = await _db.Users.FindAsync("REC-A");
        recruiter!.DeletedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.GetRecruiterJobDetailAsync("JOB-A", "REC-A"));
    }

    [Theory]
    [InlineData(JobStatus.PAUSED)]
    [InlineData(JobStatus.CLOSED)]
    public async Task Owner_CanLoadNonPublicStatusForEditScreen(JobStatus status)
    {
        var job = await _db.Jobs.FindAsync("JOB-A");
        job!.Status = status;
        await _db.SaveChangesAsync();
        var detail = await _service.GetRecruiterJobDetailAsync("JOB-A", "REC-A");
        Assert.Equal(status.ToString(), detail!.Status);
    }

    [Fact]
    public async Task Owner_CanReadExpiredJobForReadOnlyScreen()
    {
        var job = await _db.Jobs.FindAsync("JOB-A");
        job!.Deadline = DateTime.UtcNow.AddDays(-1);
        await _db.SaveChangesAsync();
        Assert.NotNull(await _service.GetRecruiterJobDetailAsync("JOB-A", "REC-A"));
    }

    [Fact]
    public async Task SoftDeletedJob_IsNotVisible()
    {
        var job = await _db.Jobs.FindAsync("JOB-A");
        job!.DeletedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        Assert.Null(await _service.GetRecruiterJobDetailAsync("JOB-A", "REC-A"));
    }

    [Fact]
    public async Task MissingJob_ReturnsNull()
    {
        Assert.Null(await _service.GetRecruiterJobDetailAsync("UNKNOWN", "REC-A"));
    }

    [Fact]
    public async Task BlankJobId_ReturnsNull()
    {
        Assert.Null(await _service.GetRecruiterJobDetailAsync("   ", "REC-A"));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
