
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Recruitment.Api.Data;
using Recruitment.Api.Dtos.Job;
using Recruitment.Api.Entities;
using Recruitment.Api.Services.Auth;

namespace Recruitment.Api.Tests;

public class CloseJobHttpIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public CloseJobHttpIntegrationTests()
    {
        // Database riêng cho mỗi bài test.
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        // Khởi động ứng dụng ASP.NET Core thật
        // nhưng thay SQL Server bằng SQLite.
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");

                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Jwt:Key"] =
                                "SCRUM-195-Test-Key-For-Integration-2026-Secure",
                            ["Jwt:Issuer"] = "RecruitmentApi",
                            ["Jwt:Audience"] = "RecruitmentClient"
                        });
                });

                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<
                        DbContextOptions<AppDbContext>>();

                    services.RemoveAll<AppDbContext>();

                    services.AddDbContext<AppDbContext>(
                        options => options.UseSqlite(_connection));
                });
            });

        // HTTPS để tránh middleware chuyển hướng.
        _client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });

        SeedDatabase();
    }

    private void SeedDatabase()
    {
        using var scope = _factory.Services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        db.Database.EnsureCreated();

        db.Companies.Add(new Company
        {
            CompanyId = "COMP-01",
            Name = "Test Company"
        });

        db.Categories.Add(new Category
        {
            CategoryId = "CAT-01",
            Name = "IT"
        });

        db.Users.AddRange(
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
                Email = "candidate@test.com",
                FullName = "Candidate",
                RoleId = "CANDIDATE"
            },
            new User
            {
                UserId = "REC-LOCKED",
                Username = "recruiterlocked",
                Email = "locked@test.com",
                FullName = "Locked Recruiter",
                RoleId = "RECRUITER",
                CompanyId = "COMP-01",
                AccountStatus = AccountStatus.LOCKED
            }
        );

        db.Jobs.AddRange(
            CreateJob("JOB-OPEN", "REC-01", JobStatus.OPEN),
            CreateJob("JOB-OTHER", "REC-02", JobStatus.OPEN),
            CreateJob("JOB-PAUSED", "REC-01", JobStatus.PAUSED),
            CreateJob("JOB-CLOSED", "REC-01", JobStatus.CLOSED),
            CreateJob(
                "JOB-DELETED",
                "REC-01",
                JobStatus.OPEN,
                deleted: true)
        );

        db.SaveChanges();
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
            Location = "Ho Chi Minh",
            Experience = "2 years",
            NumberOfPositions = 2,
            RecruiterId = recruiterId,
            CompanyId = "COMP-01",
            CategoryId = "CAT-01",
            Status = status,
            Deadline = DateTime.UtcNow.AddDays(30),
            DeletedAtUtc = deleted ? DateTime.UtcNow : null
        };
    }

    // Tạo JWT thật bằng JwtTokenService của ứng dụng.
    private string CreateToken(string userId)
    {
        using var scope = _factory.Services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var user = db.Users
            .AsNoTracking()
            .Single(u => u.UserId == userId);

        var tokenService = scope.ServiceProvider
            .GetRequiredService<JwtTokenService>();

        return tokenService.CreateToken(user).AccessToken;
    }

    // Gửi HTTP PATCH qua ASP.NET Core TestServer.
    private async Task<HttpResponseMessage> CloseAsync(
        string jobId,
        string? userId = null)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"/api/recruiter/jobs/{jobId}/close");

        if (userId != null)
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    CreateToken(userId));
        }

        return await _client.SendAsync(request);
    }

    // Đọc trạng thái Job trực tiếp từ database test.
    private async Task<JobStatus> GetStatusAsync(string jobId)
    {
        using var scope = _factory.Services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        return await db.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.Status)
            .SingleAsync();
    }

    // TEST 1 - Guest phải nhận 401.
    [Fact]
    public async Task Guest_CloseJob_Returns401()
    {
        using var response = await CloseAsync("JOB-OPEN");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);

        Assert.Equal(
            JobStatus.OPEN,
            await GetStatusAsync("JOB-OPEN"));
    }

    // TEST 2 - Candidate phải nhận 403.
    [Fact]
    public async Task Candidate_CloseJob_Returns403()
    {
        using var response = await CloseAsync(
            "JOB-OPEN", "CAN-01");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            response.StatusCode);

        Assert.Equal(
            JobStatus.OPEN,
            await GetStatusAsync("JOB-OPEN"));
    }

    // TEST 3 - Recruiter sở hữu Job đóng thành công.
    [Fact]
    public async Task Owner_CloseJob_Returns200AndPersists()
    {
        using var response = await CloseAsync(
            "JOB-OPEN", "REC-01");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body =
            await response.Content.ReadFromJsonAsync<CloseJobResponse>();

        Assert.NotNull(body);
        Assert.Equal("JOB-OPEN", body.JobId);
        Assert.Equal("CLOSED", body.Status);
        Assert.False(body.AlreadyClosed);

        Assert.Equal(
            JobStatus.CLOSED,
            await GetStatusAsync("JOB-OPEN"));
    }

    // TEST 4 - Đóng Job lần hai vẫn thành công.
    [Fact]
    public async Task Owner_CloseTwice_IsIdempotent()
    {
        using var first = await CloseAsync(
            "JOB-OPEN", "REC-01");

        using var second = await CloseAsync(
            "JOB-OPEN", "REC-01");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var body =
            await second.Content.ReadFromJsonAsync<CloseJobResponse>();

        Assert.NotNull(body);
        Assert.True(body.AlreadyClosed);
        Assert.Equal("CLOSED", body.Status);
    }

    // TEST 5 - Recruiter khác không thể đóng Job.
    [Fact]
    public async Task OtherRecruiter_CloseJob_Returns404()
    {
        using var response = await CloseAsync(
            "JOB-OPEN", "REC-02");

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);

        Assert.Equal(
            JobStatus.OPEN,
            await GetStatusAsync("JOB-OPEN"));
    }

    // TEST 6 - Không được đóng Job PAUSED.
    [Fact]
    public async Task PausedJob_Close_Returns409()
    {
        using var response = await CloseAsync(
            "JOB-PAUSED", "REC-01");

        Assert.Equal(
            HttpStatusCode.Conflict,
            response.StatusCode);

        Assert.Equal(
            JobStatus.PAUSED,
            await GetStatusAsync("JOB-PAUSED"));
    }

    // TEST 7 - Recruiter bị khóa nhận 403.
    [Fact]
    public async Task LockedRecruiter_CloseJob_Returns403()
    {
        using var response = await CloseAsync(
            "JOB-OPEN", "REC-LOCKED");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            response.StatusCode);

        Assert.Equal(
            JobStatus.OPEN,
            await GetStatusAsync("JOB-OPEN"));
    }

    // TEST 8 - Không được đóng Job đã xóa mềm.
    [Fact]
    public async Task DeletedJob_Close_Returns404()
    {
        using var response = await CloseAsync(
            "JOB-DELETED", "REC-01");

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    // TEST 9 - Job không tồn tại nhận 404.
    [Fact]
    public async Task MissingJob_Close_Returns404()
    {
        using var response = await CloseAsync(
            "NOT-EXIST", "REC-01");

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        _connection.Dispose();
    }
}
