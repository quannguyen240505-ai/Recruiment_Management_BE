
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Recruitment.Api.Controllers;
using Recruitment.Api.Data;
using Recruitment.Api.Dtos.Job;
using Recruitment.Api.Entities;
using Recruitment.Api.Services.Jobs;

namespace Recruitment.Api.Tests;

public class CloseJobTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly CloseJobService _service;

    public CloseJobTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        SeedData();
        _service = new CloseJobService(_db);
    }

    private static Job CreateJob(
        string jobId,
        string recruiterId,
        JobStatus status,
        bool deleted = false)
    {
        return new Job
        {
            JobId = jobId,
            Title = "Backend Developer",
            Description = "Development",
            Requirements = "C#",
            Location = "Ha Noi",
            Experience = "2 years",
            NumberOfPositions = 2,
            RecruiterId = recruiterId,
            CompanyId = "COMP-01",
            CategoryId = "CAT-01",
            Status = status,
            Deadline = DateTime.UtcNow.AddDays(30),
            DeletedAtUtc = deleted
                ? DateTime.UtcNow
                : null
        };
    }

    private void SeedData()
    {
        _db.Companies.Add(new Company
        {
            CompanyId = "COMP-01",
            Name = "Test Company"
        });

        _db.Categories.Add(new Category
        {
            CategoryId = "CAT-01",
            Name = "IT"
        });

        _db.Users.AddRange(
            new User
            {
                UserId = "REC-01",
                Username = "recruiter1",
                Email = "rec1@test.com",
                FullName = "Recruiter One",
                RoleId = "RECRUITER",
                CompanyId = "COMP-01"
            },
            new User
            {
                UserId = "REC-02",
                Username = "recruiter2",
                Email = "rec2@test.com",
                FullName = "Recruiter Two",
                RoleId = "RECRUITER",
                CompanyId = "COMP-01"
            },
            new User
            {
                UserId = "CAN-01",
                Username = "candidate1",
                Email = "can1@test.com",
                FullName = "Candidate",
                RoleId = "CANDIDATE"
            },
            new User
            {
                UserId = "REC-LOCKED",
                Username = "locked1",
                Email = "locked@test.com",
                FullName = "Locked Recruiter",
                RoleId = "RECRUITER",
                CompanyId = "COMP-01",
                AccountStatus = AccountStatus.LOCKED
            }
        );

        _db.Jobs.AddRange(
            CreateJob("JOB-OPEN", "REC-01", JobStatus.OPEN),
            CreateJob("JOB-OTHER", "REC-02", JobStatus.OPEN),
            CreateJob("JOB-CLOSED", "REC-01", JobStatus.CLOSED),
            CreateJob("JOB-PAUSED", "REC-01", JobStatus.PAUSED),
            CreateJob("JOB-DELETED", "REC-01", JobStatus.OPEN, true)
        );

        _db.SaveChanges();
    }

    private CloseJobController CreateController(string? userId)
    {
        var claims = userId == null
            ? Array.Empty<Claim>()
            : new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId)
            };

        return new CloseJobController(_service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(
                        new ClaimsIdentity(claims, "Test"))
                }
            }
        };
    }

    // 1. Đóng Job OPEN thành công
    [Fact]
    public async Task CloseOpenJob_ChangesStatusToClosed()
    {
        var result = await _service.CloseJobAsync(
            "REC-01", "JOB-OPEN");

        Assert.NotNull(result);
        Assert.Equal("CLOSED", result.Status);
        Assert.False(result.AlreadyClosed);

        var savedJob = await _db.Jobs
            .AsNoTracking()
            .SingleAsync(j => j.JobId == "JOB-OPEN");

        Assert.Equal(JobStatus.CLOSED, savedJob.Status);
    }

    // 2. Đóng lại không làm sai trạng thái
    [Fact]
    public async Task CloseTwice_IsIdempotent()
    {
        var first = await _service.CloseJobAsync(
            "REC-01", "JOB-OPEN");

        var second = await _service.CloseJobAsync(
            "REC-01", "JOB-OPEN");

        Assert.NotNull(first);
        Assert.NotNull(second);

        Assert.False(first.AlreadyClosed);
        Assert.True(second.AlreadyClosed);
        Assert.Equal("CLOSED", second.Status);
    }

    // 3. Job đã đóng sẵn
    [Fact]
    public async Task AlreadyClosed_ReturnsSuccess()
    {
        var result = await _service.CloseJobAsync(
            "REC-01", "JOB-CLOSED");

        Assert.NotNull(result);
        Assert.True(result.AlreadyClosed);
    }

    // 4. Recruiter khác không được đóng Job
    [Fact]
    public async Task OtherRecruiter_CannotCloseJob()
    {
        var result = await _service.CloseJobAsync(
            "REC-02", "JOB-OPEN");

        Assert.Null(result);

        var job = await _db.Jobs
            .AsNoTracking()
            .SingleAsync(j => j.JobId == "JOB-OPEN");

        Assert.Equal(JobStatus.OPEN, job.Status);
    }

    // 5. Candidate không được đóng Job
    [Fact]
    public async Task Candidate_CannotCloseJob()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.CloseJobAsync(
                "CAN-01", "JOB-OPEN"));
    }

    // 6. Recruiter bị khóa không được đóng
    [Fact]
    public async Task LockedRecruiter_CannotCloseJob()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.CloseJobAsync(
                "REC-LOCKED", "JOB-OPEN"));
    }

    // 7. Job bị xóa mềm không được đóng
    [Fact]
    public async Task DeletedJob_ReturnsNull()
    {
        var result = await _service.CloseJobAsync(
            "REC-01", "JOB-DELETED");

        Assert.Null(result);
    }

    // 8. Không đóng trực tiếp Job PAUSED
    [Fact]
    public async Task PausedJob_ThrowsConflict()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CloseJobAsync(
                "REC-01", "JOB-PAUSED"));
    }

    // 9. Job không tồn tại
    [Fact]
    public async Task MissingJob_ReturnsNull()
    {
        var result = await _service.CloseJobAsync(
            "REC-01", "NOT-EXIST");

        Assert.Null(result);
    }

    // 10. JobId rỗng
    [Fact]
    public async Task EmptyJobId_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.CloseJobAsync(
                "REC-01", ""));
    }

    // 11. RecruiterId rỗng
    [Fact]
    public async Task EmptyRecruiterId_ThrowsUnauthorized()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.CloseJobAsync(
                "", "JOB-OPEN"));
    }

    // 12. Controller: Thiếu identity
    [Fact]
    public async Task Controller_GuestReturns401()
    {
        var controller = CreateController(null);

        var result = await controller.CloseJob("JOB-OPEN");

        Assert.IsType<UnauthorizedResult>(result);
    }

    // 13. Controller: Đóng Job thành công
    [Fact]
    public async Task Controller_OwnerReturns200()
    {
        var controller = CreateController("REC-01");

        var result = await controller.CloseJob("JOB-OPEN");

        var ok = Assert.IsType<OkObjectResult>(result);

        var response = Assert.IsType<CloseJobResponse>(
            ok.Value);

        Assert.Equal("CLOSED", response.Status);
    }

    // 14. Controller: Recruiter khác nhận 404
    [Fact]
    public async Task Controller_OtherRecruiterReturns404()
    {
        var controller = CreateController("REC-02");

        var result = await controller.CloseJob("JOB-OPEN");

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // 15. Controller: PAUSED nhận 409
    [Fact]
    public async Task Controller_PausedJobReturns409()
    {
        var controller = CreateController("REC-01");

        var result = await controller.CloseJob("JOB-PAUSED");

        var conflict = Assert.IsType<ConflictObjectResult>(
            result);

        Assert.Equal(409, conflict.StatusCode);
    }

    // 16. Controller yêu cầu Recruiter role
    [Fact]
    public void Controller_RequiresRecruiterPolicy()
    {
        var attribute = Assert.Single(
            typeof(CloseJobController)
                .GetCustomAttributes(
                    typeof(AuthorizeAttribute), true)
                .Cast<AuthorizeAttribute>());

        Assert.Equal(
            "RequireRecruiterRole",
            attribute.Policy);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
