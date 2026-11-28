
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Recruitment.Api.Controllers;
using Recruitment.Api.Data;
using Recruitment.Api.Dtos.Application;
using Recruitment.Api.Entities;
using Recruitment.Api.Services.Applications;

namespace Recruitment.Api.Tests;

public class MyApplicationsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly ApplicationService _service;

    public MyApplicationsTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        SeedData();
        _service = new ApplicationService(_db);
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
                Username = "recruiter-test",
                Email = "recruiter@test.com",
                FullName = "Recruiter",
                RoleId = "RECRUITER",
                CompanyId = "COMP-01"
            },
            new User
            {
                UserId = "CAN-01",
                Username = "candidate-one",
                Email = "candidate1@test.com",
                FullName = "Candidate One",
                RoleId = "CANDIDATE"
            },
            new User
            {
                UserId = "CAN-02",
                Username = "candidate-two",
                Email = "candidate2@test.com",
                FullName = "Candidate Two",
                RoleId = "CANDIDATE"
            }
        );

        _db.SaveChanges();

        _db.Jobs.AddRange(
            new Job
            {
                JobId = "JOB-01",
                Title = "Backend Developer",
                Description = "Backend development",
                Requirements = "C#",
                Location = "Ha Noi",
                Experience = "2 years",
                NumberOfPositions = 2,
                RecruiterId = "REC-01",
                CompanyId = "COMP-01",
                CategoryId = "CAT-01",
                Status = JobStatus.OPEN,
                Deadline = DateTime.UtcNow.AddDays(30)
            },
            new Job
            {
                JobId = "JOB-02",
                Title = "Frontend Developer",
                Description = "Frontend development",
                Requirements = "Angular",
                Location = "Da Nang",
                Experience = "1 year",
                NumberOfPositions = 1,
                RecruiterId = "REC-01",
                CompanyId = "COMP-01",
                CategoryId = "CAT-01",
                Status = JobStatus.CLOSED,
                Deadline = DateTime.UtcNow.AddDays(30)
            }
        );

        _db.SaveChanges();

        _db.Applications.AddRange(
            new Application
            {
                ApplicationId = "APP-01",
                CandidateId = "CAN-01",
                JobId = "JOB-01",
                CvId = "CV-01",
                Status = ApplicationStatus.APPLIED,
                AppliedAtUtc = new DateTime(2026, 11, 1)
            },
            new Application
            {
                ApplicationId = "APP-02",
                CandidateId = "CAN-02",
                JobId = "JOB-01",
                CvId = "CV-02",
                Status = ApplicationStatus.UNDER_REVIEW,
                AppliedAtUtc = new DateTime(2026, 11, 2)
            },
            new Application
            {
                ApplicationId = "APP-03",
                CandidateId = "CAN-01",
                JobId = "JOB-02",
                CvId = "CV-03",
                CoverLetter = "I am interested",
                Status = ApplicationStatus.SHORTLISTED,
                AppliedAtUtc = new DateTime(2026, 11, 3)
            }
        );

        _db.SaveChanges();
    }

    private ApplicationsController CreateController(string? candidateId)
    {
        var claims = candidateId == null
            ? Array.Empty<Claim>()
            : new[]
            {
                new Claim(ClaimTypes.NameIdentifier, candidateId),
                new Claim(ClaimTypes.Role, "CANDIDATE")
            };

        return new ApplicationsController(_service)
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

    [Fact]
    public async Task List_ReturnsOnlyOwnApplications()
    {
        var result = await _service
            .GetMyApplicationsAsync("CAN-01");

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item =>
            Assert.NotEqual("APP-02", item.ApplicationId));
    }

    [Fact]
    public async Task List_ReturnsNewestFirst()
    {
        var result = await _service
            .GetMyApplicationsAsync("CAN-01");

        Assert.Equal("APP-03", result.Items[0].ApplicationId);
        Assert.Equal("APP-01", result.Items[1].ApplicationId);
    }

    [Fact]
    public async Task List_ContainsJobAndCompany()
    {
        var result = await _service
            .GetMyApplicationsAsync("CAN-01");

        var item = Assert.Single(
            result.Items, x => x.ApplicationId == "APP-01");

        Assert.Equal("Backend Developer", item.JobTitle);
        Assert.Equal("Test Company", item.CompanyName);
        Assert.Equal("CV-01", item.CvId);
        Assert.Equal("APPLIED", item.Status);
    }

    [Fact]
    public async Task List_IncludesClosedJobs()
    {
        var result = await _service
            .GetMyApplicationsAsync("CAN-01");

        Assert.Contains(result.Items,
            x => x.JobId == "JOB-02");
    }

    [Fact]
    public async Task List_SupportsPagination()
    {
        var result = await _service
            .GetMyApplicationsAsync("CAN-01", 2, 1);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Page);
        Assert.Equal(1, result.PageSize);
        Assert.Single(result.Items);
        Assert.Equal("APP-01", result.Items[0].ApplicationId);
    }

    [Fact]
    public async Task List_UnknownCandidateReturnsEmpty()
    {
        var result = await _service
            .GetMyApplicationsAsync("CAN-NOT-FOUND");

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task List_InvalidPaginationThrows(
        int page, int pageSize)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.GetMyApplicationsAsync(
                "CAN-01", page, pageSize));
    }

    [Fact]
    public async Task Detail_OwnerCanView()
    {
        var result = await _service
            .GetMyApplicationDetailAsync("CAN-01", "APP-03");

        Assert.NotNull(result);
        Assert.Equal("Frontend Developer", result.JobTitle);
        Assert.Equal("SHORTLISTED", result.Status);
        Assert.Equal("I am interested", result.CoverLetter);
    }

    [Fact]
    public async Task Detail_OtherCandidateCannotView()
    {
        var result = await _service
            .GetMyApplicationDetailAsync("CAN-02", "APP-01");

        Assert.Null(result);
    }

    [Fact]
    public async Task Detail_NotFoundReturnsNull()
    {
        var result = await _service
            .GetMyApplicationDetailAsync("CAN-01", "MISSING");

        Assert.Null(result);
    }

    [Fact]
    public async Task Controller_MissingIdentityReturns401()
    {
        var controller = CreateController(null);

        var result = await controller.GetMyApplications();

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task Controller_ListReturns200()
    {
        var controller = CreateController("CAN-01");

        var result = await controller.GetMyApplications();

        var ok = Assert.IsType<OkObjectResult>(result);
        var data = Assert.IsType<MyApplicationsResponse>(ok.Value);

        Assert.Equal(2, data.TotalCount);
    }

    [Fact]
    public async Task Controller_InvalidPageReturns400()
    {
        var controller = CreateController("CAN-01");

        var result = await controller.GetMyApplications(0, 10);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Controller_ForeignDetailReturns404()
    {
        var controller = CreateController("CAN-02");

        var result = await controller
            .GetMyApplicationDetail("APP-01");

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void Controller_RequiresCandidateRole()
    {
        var attribute = Assert.Single(
            typeof(ApplicationsController)
                .GetCustomAttributes(
                    typeof(AuthorizeAttribute), true)
                .Cast<AuthorizeAttribute>());

        Assert.Equal("RequireCandidateRole", attribute.Policy);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
