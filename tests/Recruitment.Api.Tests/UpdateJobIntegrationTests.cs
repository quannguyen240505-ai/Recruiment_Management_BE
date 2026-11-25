using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Recruitment.Api.Data;
using Recruitment.Api.Dtos.Job;
using Recruitment.Api.Entities;
using Recruitment.Api.Services.Jobs;
using Xunit;

namespace Recruitment.Api.Tests;

// SCRUM-191: Integration tests for the REAL JobService against SQLite in-memory.
// These tests are separate from the 42 DTO/Controller unit tests.
public sealed class UpdateJobIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly JobService _service;

    public UpdateJobIntegrationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _service = new JobService(_db);

        _db.Companies.Add(new Company
        {
            CompanyId = "COMP-01",
            Name = "Company 01"
        });
        _db.Categories.AddRange(
            new Category { CategoryId = "CAT-01", Name = "IT" },
            new Category { CategoryId = "CAT-02", Name = "Design" });
        _db.Skills.AddRange(
            new Skill { SkillId = "SKILL-01", Name = "C#" },
            new Skill { SkillId = "SKILL-02", Name = "SQL" },
            new Skill { SkillId = "SKILL-03", Name = "Angular" });

        // Recruiter A and B belong to the SAME company, so ownership tests
        // also verify that company membership does not grant editing rights.
        _db.Users.AddRange(
            MakeUser("REC-01", "RECRUITER", "COMP-01", AccountStatus.ACTIVE),
            MakeUser("REC-02", "RECRUITER", "COMP-01", AccountStatus.ACTIVE),
            MakeUser("CAN-01", "CANDIDATE", null, AccountStatus.ACTIVE));
        _db.SaveChanges();
    }

    private static User MakeUser(
        string id, string role, string? companyId, AccountStatus status)
        => new()
        {
            UserId = id,
            Username = id.ToLowerInvariant(),
            Email = $"{id.ToLowerInvariant()}@test.local",
            FullName = id,
            RoleId = role,
            CompanyId = companyId,
            AccountStatus = status,
            CreatedAtUtc = DateTime.UtcNow
        };

    private static CreateJobRequest ValidCreate() => new()
    {
        Title = "Original Job",
        Description = "Original description",
        Requirements = "Original requirements",
        Location = "Ha Noi",
        JobType = JobType.FULL_TIME,
        Experience = "2 years",
        NumberOfPositions = 2,
        SalaryMin = 1000m,
        SalaryMax = 2000m,
        Deadline = DateTime.UtcNow.Date.AddDays(30),
        CategoryId = "CAT-01",
        SkillIds = new List<string> { "SKILL-01" }
    };

    private static UpdateJobRequest ValidUpdate() => new()
    {
        Title = "Updated Job",
        Description = "Updated description",
        Requirements = "Updated requirements",
        Location = "Da Nang",
        JobType = JobType.PART_TIME,
        Experience = "4 years",
        NumberOfPositions = 3,
        SalaryMin = 1500m,
        SalaryMax = 3000m,
        Deadline = DateTime.UtcNow.Date.AddDays(45),
        CategoryId = "CAT-02",
        SkillIds = new List<string> { "SKILL-02" },
    };

    private async Task<Job> CreateJobAsync()
    {
        var result = await _service.CreateJobAsync(ValidCreate(), "REC-01");
        return await _db.Jobs.SingleAsync(j => j.JobId == result.JobId);
    }

    // 1. Success and persistence.
    [Fact]
    public async Task UpdateJob_ValidRequest_PersistsEditableFields()
    {
        var job = await CreateJobAsync();
        var response = await _service.UpdateJobAsync(job.JobId, ValidUpdate(), "REC-01");
        var persisted = await _db.Jobs.AsNoTracking().SingleAsync(j => j.JobId == job.JobId);

        Assert.Equal("Updated Job", response.Title);
        Assert.Equal("Updated Job", persisted.Title);
        Assert.Equal("Updated description", persisted.Description);
        Assert.Equal("Updated requirements", persisted.Requirements);
        Assert.Equal("Da Nang", persisted.Location);
        Assert.Equal(JobType.PART_TIME, persisted.JobType);
        Assert.Equal("4 years", persisted.Experience);
        Assert.Equal(3, persisted.NumberOfPositions);
        Assert.Equal(1500m, persisted.SalaryMin);
        Assert.Equal(3000m, persisted.SalaryMax);
        Assert.Equal("CAT-02", persisted.CategoryId);
        Assert.True(persisted.Deadline.Date > DateTime.UtcNow.Date);
    }

    // 2. Ownership and system fields must remain immutable.
    [Fact]
    public async Task UpdateJob_ValidRequest_PreservesOwnerCompanyIdStatusAndCreatedAt()
    {
        var job = await CreateJobAsync();
        var createdAt = job.CreatedAt;
        await _service.UpdateJobAsync(job.JobId, ValidUpdate(), "REC-01");
        var persisted = await _db.Jobs.AsNoTracking().SingleAsync(j => j.JobId == job.JobId);

        Assert.Equal("REC-01", persisted.RecruiterId);
        Assert.Equal("COMP-01", persisted.CompanyId);
        Assert.Equal(JobStatus.OPEN, persisted.Status);
        Assert.Equal(createdAt, persisted.CreatedAt);
    }

    // 3. A different recruiter in the same company is NOT the owner.
    [Fact]
    public async Task UpdateJob_OtherRecruiterInSameCompany_IsRejected()
    {
        var job = await CreateJobAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.UpdateJobAsync(job.JobId, ValidUpdate(), "REC-02"));
    }

    // 4. Only Recruiter accounts are allowed.
    [Fact]
    public async Task UpdateJob_Candidate_IsRejected()
    {
        var job = await CreateJobAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.UpdateJobAsync(job.JobId, ValidUpdate(), "CAN-01"));
    }

    // 5. Nonexistent recruiter must be rejected.
    [Fact]
    public async Task UpdateJob_UnknownRecruiter_IsRejected()
    {
        var job = await CreateJobAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.UpdateJobAsync(job.JobId, ValidUpdate(), "REC-NOT-FOUND"));
    }

    // 6. Locked recruiter cannot update.
    [Fact]
    public async Task UpdateJob_LockedRecruiter_IsRejected()
    {
        var job = await CreateJobAsync();
        var recruiter = await _db.Users.SingleAsync(u => u.UserId == "REC-01");
        recruiter.AccountStatus = AccountStatus.LOCKED;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.UpdateJobAsync(job.JobId, ValidUpdate(), "REC-01"));
    }

    // 7. Soft-deleted recruiter cannot update.
    [Fact]
    public async Task UpdateJob_DeletedRecruiter_IsRejected()
    {
        var job = await CreateJobAsync();
        var recruiter = await _db.Users.SingleAsync(u => u.UserId == "REC-01");
        recruiter.DeletedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.UpdateJobAsync(job.JobId, ValidUpdate(), "REC-01"));
    }

    // 8. Unknown Job ID.
    [Fact]
    public async Task UpdateJob_UnknownJob_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.UpdateJobAsync("UNKNOWN-JOB", ValidUpdate(), "REC-01"));
    }

    // 9. Soft-deleted jobs cannot be updated.
    [Fact]
    public async Task UpdateJob_SoftDeletedJob_ThrowsNotFound()
    {
        var job = await CreateJobAsync();
        job.DeletedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.UpdateJobAsync(job.JobId, ValidUpdate(), "REC-01"));
    }

    // 10. Closed jobs cannot be edited under the current business rule.
    [Fact]
    public async Task UpdateJob_ClosedJob_ThrowsConflict()
    {
        var job = await CreateJobAsync();
        job.Status = JobStatus.CLOSED;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateJobAsync(job.JobId, ValidUpdate(), "REC-01"));
    }

    // 11. An expired existing Job cannot be renewed via Edit.
    [Fact]
    public async Task UpdateJob_ExpiredExistingJob_ThrowsConflict()
    {
        var job = await CreateJobAsync();
        job.Deadline = DateTime.UtcNow.Date.AddDays(-1);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateJobAsync(job.JobId, ValidUpdate(), "REC-01"));
    }

    // 12. PAUSED is editable, but editing must not reopen the Job.
    [Fact]
    public async Task UpdateJob_PausedJob_RemainsPaused()
    {
        var job = await CreateJobAsync();
        job.Status = JobStatus.PAUSED;
        await _db.SaveChangesAsync();

        var result = await _service.UpdateJobAsync(job.JobId, ValidUpdate(), "REC-01");
        Assert.Equal("PAUSED", result.Status);
        Assert.Equal(JobStatus.PAUSED, (await _db.Jobs.FindAsync(job.JobId))!.Status);
    }

    // 13-18. Six distinct required-field validation cases.
    [Theory]
    [InlineData("Title")]
    [InlineData("Description")]
    [InlineData("Requirements")]
    [InlineData("Location")]
    [InlineData("Experience")]
    [InlineData("CategoryId")]
    public async Task UpdateJob_WhitespaceRequiredField_IsRejected(string field)
    {
        var job = await CreateJobAsync();
        var request = ValidUpdate();
        switch (field)
        {
            case "Title": request.Title = "  "; break;
            case "Description": request.Description = "  "; break;
            case "Requirements": request.Requirements = "  "; break;
            case "Location": request.Location = "  "; break;
            case "Experience": request.Experience = "  "; break;
            case "CategoryId": request.CategoryId = "  "; break;
        }

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateJobAsync(job.JobId, request, "REC-01"));
    }

    // 19. Missing enum is rejected even when calling service directly.
    [Fact]
    public async Task UpdateJob_NullJobType_IsRejected()
    {
        var job = await CreateJobAsync();
        var request = ValidUpdate();
        request.JobType = null;
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateJobAsync(job.JobId, request, "REC-01"));
    }

    // 20. Unknown enum value is rejected.
    [Fact]
    public async Task UpdateJob_UnknownJobType_IsRejected()
    {
        var job = await CreateJobAsync();
        var request = ValidUpdate();
        request.JobType = (JobType)999;
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateJobAsync(job.JobId, request, "REC-01"));
    }

    // 21-22. Positions must be positive.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task UpdateJob_NonPositivePositions_IsRejected(int positions)
    {
        var job = await CreateJobAsync();
        var request = ValidUpdate();
        request.NumberOfPositions = positions;
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateJobAsync(job.JobId, request, "REC-01"));
    }

    // 23-24. Reject negative salary in either field.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpdateJob_NegativeSalary_IsRejected(bool minimum)
    {
        var job = await CreateJobAsync();
        var request = ValidUpdate();
        if (minimum) request.SalaryMin = -1;
        else request.SalaryMax = -1;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateJobAsync(job.JobId, request, "REC-01"));
    }

    // 25. SalaryMax must be at least SalaryMin.
    [Fact]
    public async Task UpdateJob_MaxSalaryLessThanMin_IsRejected()
    {
        var job = await CreateJobAsync();
        var request = ValidUpdate();
        request.SalaryMin = 5000;
        request.SalaryMax = 1000;
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateJobAsync(job.JobId, request, "REC-01"));
    }

    // 26. Both salary endpoints can be null (same as Create Job).
    [Fact]
    public async Task UpdateJob_OptionalSalary_CanBeNull()
    {
        var job = await CreateJobAsync();
        var request = ValidUpdate();
        request.SalaryMin = null;
        request.SalaryMax = null;
        await _service.UpdateJobAsync(job.JobId, request, "REC-01");
        var persisted = await _db.Jobs.AsNoTracking().SingleAsync(j => j.JobId == job.JobId);
        Assert.Null(persisted.SalaryMin);
        Assert.Null(persisted.SalaryMax);
    }

    // 27-28. Deadline today/past are both invalid.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task UpdateJob_NonFutureNewDeadline_IsRejected(int daysFromToday)
    {
        var job = await CreateJobAsync();
        var request = ValidUpdate();
        request.Deadline = DateTime.UtcNow.Date.AddDays(daysFromToday);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateJobAsync(job.JobId, request, "REC-01"));
    }

    // 29. Category must exist in the database.
    [Fact]
    public async Task UpdateJob_UnknownCategory_IsRejected()
    {
        var job = await CreateJobAsync();
        var request = ValidUpdate();
        request.CategoryId = "CAT-UNKNOWN";
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateJobAsync(job.JobId, request, "REC-01"));
    }

    // 30. Null skills list must not cause null-reference exceptions.
    [Fact]
    public async Task UpdateJob_NullSkillIds_IsRejected()
    {
        var job = await CreateJobAsync();
        var request = ValidUpdate();
        request.SkillIds = null!;
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateJobAsync(job.JobId, request, "REC-01"));
    }

    // 31. An empty skills list is not allowed.
    [Fact]
    public async Task UpdateJob_EmptySkillIds_IsRejected()
    {
        var job = await CreateJobAsync();
        var request = ValidUpdate();
        request.SkillIds = new List<string>();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateJobAsync(job.JobId, request, "REC-01"));
    }

    // 32. An individual blank skill ID is invalid.
    [Fact]
    public async Task UpdateJob_BlankSkillId_IsRejected()
    {
        var job = await CreateJobAsync();
        var request = ValidUpdate();
        request.SkillIds = new List<string> { "SKILL-01", " " };
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateJobAsync(job.JobId, request, "REC-01"));
    }

    // 33. Unknown skills are rejected.
    [Fact]
    public async Task UpdateJob_UnknownSkill_IsRejected()
    {
        var job = await CreateJobAsync();
        var request = ValidUpdate();
        request.SkillIds = new List<string> { "SKILL-UNKNOWN" };
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateJobAsync(job.JobId, request, "REC-01"));
    }

    // 34. Duplicate skills are rejected.
    [Fact]
    public async Task UpdateJob_DuplicateSkills_AreRejected()
    {
        var job = await CreateJobAsync();
        var request = ValidUpdate();
        request.SkillIds = new List<string> { "SKILL-01", "SKILL-01" };
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateJobAsync(job.JobId, request, "REC-01"));
    }

    // 35. Replacing a skill deletes old association and adds new one.
    [Fact]
    public async Task UpdateJob_ReplacesJobSkillsInDatabase()
    {
        var job = await CreateJobAsync();
        await _service.UpdateJobAsync(job.JobId, ValidUpdate(), "REC-01");
        var skills = await _db.JobSkills.AsNoTracking()
            .Where(s => s.JobId == job.JobId)
            .Select(s => s.SkillId)
            .ToListAsync();

        Assert.Single(skills);
        Assert.Equal("SKILL-02", skills[0]);
    }

    // 36. Adding more skills preserves the existing association.
    [Fact]
    public async Task UpdateJob_AddingSkills_PreservesExistingSkill()
    {
        var job = await CreateJobAsync();
        var request = ValidUpdate();
        request.SkillIds = new List<string> { "SKILL-01", "SKILL-02", "SKILL-03" };
        await _service.UpdateJobAsync(job.JobId, request, "REC-01");
        var skillIds = await _db.JobSkills.AsNoTracking()
            .Where(s => s.JobId == job.JobId)
            .Select(s => s.SkillId)
            .ToListAsync();

        Assert.Equal(3, skillIds.Count);
        Assert.Contains("SKILL-01", skillIds);
        Assert.Contains("SKILL-02", skillIds);
        Assert.Contains("SKILL-03", skillIds);
    }

    // 37. Keeping the same skill should not create duplicate join rows.
    [Fact]
    public async Task UpdateJob_SameSkill_DoesNotCreateDuplicateJoinRow()
    {
        var job = await CreateJobAsync();
        var request = ValidUpdate();
        request.SkillIds = new List<string> { "SKILL-01" };
        await _service.UpdateJobAsync(job.JobId, request, "REC-01");
        var count = await _db.JobSkills.CountAsync(s => s.JobId == job.JobId);
        Assert.Equal(1, count);
    }

    // 38. Failed validation must not mutate persisted Job fields or skills.
    [Fact]
    public async Task UpdateJob_InvalidSalary_LeavesJobAndSkillsUnchanged()
    {
        var job = await CreateJobAsync();
        var request = ValidUpdate();
        request.SalaryMin = 4000;
        request.SalaryMax = 3000;
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateJobAsync(job.JobId, request, "REC-01"));

        var persisted = await _db.Jobs.AsNoTracking().SingleAsync(j => j.JobId == job.JobId);
        var skillIds = await _db.JobSkills.AsNoTracking()
            .Where(s => s.JobId == job.JobId)
            .Select(s => s.SkillId)
            .ToListAsync();
        Assert.Equal("Original Job", persisted.Title);
        Assert.Equal(1000m, persisted.SalaryMin);
        Assert.Equal(2000m, persisted.SalaryMax);
        Assert.Single(skillIds);
        Assert.Equal("SKILL-01", skillIds[0]);
    }

    // 39. Failed ownership check must not mutate persisted data.
    [Fact]
    public async Task UpdateJob_NonOwnerAttempt_LeavesDataUnchanged()
    {
        var job = await CreateJobAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.UpdateJobAsync(job.JobId, ValidUpdate(), "REC-02"));

        var persisted = await _db.Jobs.AsNoTracking().SingleAsync(j => j.JobId == job.JobId);
        Assert.Equal("Original Job", persisted.Title);
        Assert.Equal("REC-01", persisted.RecruiterId);
    }

    // 40. Multiple updates should persist the latest valid values.
    [Fact]
    public async Task UpdateJob_TwoConsecutiveUpdates_PersistsLatestValues()
    {
        var job = await CreateJobAsync();
        var first = ValidUpdate();
        first.Title = "First update";
        await _service.UpdateJobAsync(job.JobId, first, "REC-01");

        var second = ValidUpdate();
        second.Title = "Second update";
        second.SkillIds = new List<string> { "SKILL-03" };
        await _service.UpdateJobAsync(job.JobId, second, "REC-01");

        var persisted = await _db.Jobs.AsNoTracking().SingleAsync(j => j.JobId == job.JobId);
        var skillIds = await _db.JobSkills.AsNoTracking()
            .Where(s => s.JobId == job.JobId)
            .Select(s => s.SkillId)
            .ToListAsync();
        Assert.Equal("Second update", persisted.Title);
        Assert.Single(skillIds);
        Assert.Equal("SKILL-03", skillIds[0]);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
