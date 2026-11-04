using System.ComponentModel.DataAnnotations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Recruitment.Api.Data;
using Recruitment.Api.Dtos.Job;
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

    [Fact]
    public async Task CreateJob_RecruiterLocked_Throws()
    {
        var rec = await _db.Users.FindAsync("REC-01");
        rec!.AccountStatus = AccountStatus.LOCKED;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _jobService.CreateJobAsync(GetValidRequest(), "REC-01"));
    }

    [Fact]
    public async Task CreateJob_CompanyNotExists_Throws()
    {
        // Lấy User đang được track bởi DbContext
        var rec = await _db.Users.FindAsync("REC-01");

        // Thay đổi CompanyId trong bộ nhớ (KHÔNG gọi SaveChangesAsync để tránh lỗi FK của SQLite)
        rec!.CompanyId = "COMP-NOT-EXIST";

        // Khi gọi Service:
        // - Nó query User từ ChangeTracker (sẽ thấy CompanyId mới)
        // - Nó query bảng Companies dưới DB (sẽ không thấy COMP-NOT-EXIST)
        // -> Ném ra InvalidOperationException
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _jobService.CreateJobAsync(GetValidRequest(), "REC-01"));

        Assert.Contains("không tồn tại trong hệ thống", ex.Message);
    }

    [Fact]
    public async Task CreateJob_NullSkillIds_Throws()
    {
        var req = GetValidRequest();
        req.SkillIds = null!;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _jobService.CreateJobAsync(req, "REC-01"));
    }

    [Fact]
    public void CreateJobRequest_InvalidJobType_ShouldFailValidation()
    {
        // Tạo request với jobType = 999 (giá trị không nằm trong Enum)
        var request = new CreateJobRequest
        {
            Title = "Test",
            Description = "Test",
            Requirements = "Test",
            Location = "Test",
            JobType = (JobType)999,
            Experience = "Test",
            NumberOfPositions = 1,
            Deadline = DateTime.UtcNow.AddDays(1),
            CategoryId = "CAT-01",
            SkillIds = new List<string> { "SKILL-01" }
        };

        // Chạy Validation bằng DataAnnotations
        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(request, context, results, true);

        // Kỳ vọng: Validate False và có lỗi liên quan đến JobType
        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains("JobType"));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
