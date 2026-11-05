using System.ComponentModel.DataAnnotations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Recruitment.Api.Data;
using Recruitment.Api.Dtos.Job;
using Recruitment.Api.Entities;
using Recruitment.Api.Services.Jobs;
using Microsoft.AspNetCore.Mvc;
using Recruitment.Api.Controllers;
using System.Text.Json;

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
        _db.Users.Add(new User { UserId = "REC-02", Username = "rec2", Email = "rec2@test.com", FullName = "Rec2", RoleId = "RECRUITER", CompanyId = "COMP-01", AccountStatus = AccountStatus.ACTIVE, CreatedAtUtc = DateTime.UtcNow });
        _db.Jobs.Add(new Job { JobId = "JOB-REC2", Title = "Job cua Rec2", Status = JobStatus.OPEN, RecruiterId = "REC-02", CompanyId = "COMP-01", CategoryId = "CAT-01", Description = "D", Requirements = "R", Location = "L", Experience = "E", NumberOfPositions = 1, Deadline = DateTime.UtcNow.AddDays(10) });
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
        _db.Users.Add(new User
        {
            UserId = "REC-NO-COMP",
            Username = "recnocomp",
            Email = "recnocomp@test.com",
            FullName = "RecNoComp",
            RoleId = "RECRUITER",
            CompanyId = null,
            AccountStatus = AccountStatus.ACTIVE,
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _jobService.CreateJobAsync(GetValidRequest(), "REC-NO-COMP"));
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

    [Fact]
    public async Task GetJobDetail_OpenJob_ReturnsDetails()
    {
        var created = await _jobService.CreateJobAsync(
            GetValidRequest(), "REC-01");

        var result = await _jobService.GetJobDetailAsync(created.JobId);

        Assert.NotNull(result);
        Assert.Equal(created.JobId, result.JobId);
        Assert.Equal("Senior Dev", result.Title);
        Assert.Equal("OPEN", result.Status);
        Assert.Equal("Test Company", result.Company.Name);

        Assert.Contains(result.Skills,
            s => s.SkillId == "SKILL-01" && s.Name == "C#");
    }

    [Fact]
    public async Task GetJobDetail_NotFound_ReturnsNull()
    {
        var result = await _jobService.GetJobDetailAsync(
            "JOB-NOT-EXIST");

        Assert.Null(result);
    }

    [Theory]
    [InlineData(JobStatus.CLOSED)]
    [InlineData(JobStatus.PAUSED)]
    public async Task GetJobDetail_NotOpen_ReturnsNull(JobStatus status)
    {
        var created = await _jobService.CreateJobAsync(
            GetValidRequest(), "REC-01");

        var job = await _db.Jobs.FindAsync(created.JobId);
        Assert.NotNull(job);

        job.Status = status;
        await _db.SaveChangesAsync();

        var result = await _jobService.GetJobDetailAsync(created.JobId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetJobDetail_ExpiredJob_ReturnsNull()
    {
        var created = await _jobService.CreateJobAsync(
            GetValidRequest(), "REC-01");

        var job = await _db.Jobs.FindAsync(created.JobId);
        Assert.NotNull(job);

        job.Deadline = DateTime.UtcNow.AddDays(-1);
        await _db.SaveChangesAsync();

        var result = await _jobService.GetJobDetailAsync(created.JobId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetJobDetail_DeadlineToday_ReturnsNull()
    {
        var created = await _jobService.CreateJobAsync(
            GetValidRequest(), "REC-01");

        var job = await _db.Jobs.FindAsync(created.JobId);
        Assert.NotNull(job);

        job.Deadline = DateTime.UtcNow.Date.AddDays(1).AddTicks(-1);
        await _db.SaveChangesAsync();

        var result = await _jobService.GetJobDetailAsync(created.JobId);

        Assert.Null(result);
    }

    [Fact]
    public async Task CreateJob_DeadlineToday_Throws()
    {
        var request = GetValidRequest();
        request.Deadline = DateTime.UtcNow.Date.AddDays(1).AddTicks(-1);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _jobService.CreateJobAsync(request, "REC-01"));
    }

    [Fact]
    public async Task GetJobDetail_DeletedJob_Returns404()
    {
        var created = await _jobService.CreateJobAsync(
            GetValidRequest(), "REC-01");

        var job = await _db.Jobs.FindAsync(created.JobId);
        Assert.NotNull(job);

        job.DeletedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var controller = new JobsController(_jobService);
        var result = await controller.GetJobDetail(created.JobId);

        var response = Assert.IsType<NotFoundObjectResult>(
            result.Result);

        Assert.Equal(404, response.StatusCode);

        var json = JsonSerializer.Serialize(response.Value);
        Assert.Contains("JOB_NOT_FOUND", json);
    }
    [Fact]
    public async Task GetMyJobs_ReturnsOnlyJobsOfLoggedInRecruiter()
    {
        await _jobService.CreateJobAsync(GetValidRequest(), "REC-01");

        var result = await _jobService.GetMyJobsAsync("REC-01", null, 1, 10);

        Assert.Equal(1, result.TotalCount);
        Assert.All(result.Items, item => Assert.NotEqual("JOB-REC2", item.JobId));
    }

    [Fact]
    public async Task GetMyJobs_ExcludesSoftDeletedJobs()
    {
        await _jobService.CreateJobAsync(GetValidRequest(), "REC-01");

        var job = await _db.Jobs.FirstAsync(j => j.RecruiterId == "REC-01");
        job.DeletedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var result = await _jobService.GetMyJobsAsync("REC-01", null, 1, 10);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetMyJobs_WithStatusFilter_ReturnsFilteredJobs()
    {
        await _jobService.CreateJobAsync(GetValidRequest(), "REC-01");

        var job = await _db.Jobs.FirstAsync(j => j.RecruiterId == "REC-01");
        job.Status = JobStatus.CLOSED;
        await _db.SaveChangesAsync();

        var resultOpen = await _jobService.GetMyJobsAsync("REC-01", JobStatus.OPEN, 1, 10);
        Assert.Empty(resultOpen.Items);

        var resultClosed = await _jobService.GetMyJobsAsync("REC-01", JobStatus.CLOSED, 1, 10);
        Assert.Single(resultClosed.Items);
    }
    [Fact]
    public async Task GetMyJobs_EmptyList_ReturnsEmpty()
    {
        _db.Users.Add(new User { UserId = "REC-03", Username = "rec3", Email = "rec3@test.com", FullName = "Rec3", RoleId = "RECRUITER", CompanyId = "COMP-01", AccountStatus = AccountStatus.ACTIVE, CreatedAtUtc = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var result = await _jobService.GetMyJobsAsync("REC-03", null, 1, 10);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task GetMyJobs_Pagination_ReturnsCorrectPage()
    {
        for (int i = 0; i < 15; i++)
        {
            await _jobService.CreateJobAsync(GetValidRequest(), "REC-01");
        }

        var page1 = await _jobService.GetMyJobsAsync("REC-01", null, 1, 10);
        Assert.Equal(10, page1.Items.Count);
        Assert.Equal(15, page1.TotalCount);
        Assert.Equal(2, page1.TotalPages);

        var page2 = await _jobService.GetMyJobsAsync("REC-01", null, 2, 10);
        Assert.Equal(5, page2.Items.Count);
    }
    [Fact]
    public async Task GetMyJobs_InvalidPagination_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _jobService.GetMyJobsAsync("REC-01", null, 0, 10));
        await Assert.ThrowsAsync<ArgumentException>(() => _jobService.GetMyJobsAsync("REC-01", null, 1, 101));
    }

    [Fact]
    public async Task GetMyJobs_UserNotRecruiter_Throws()
    {
        // Đổi role của REC-01 thành CANDIDATE
        var user = await _db.Users.FindAsync("REC-01");
        user!.RoleId = "CANDIDATE";
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _jobService.GetMyJobsAsync("REC-01", null, 1, 10));
    }
    [Fact]
    public async Task GetMyJobs_PageOverflow_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _jobService.GetMyJobsAsync(
                "REC-01", null, int.MaxValue, 100));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
