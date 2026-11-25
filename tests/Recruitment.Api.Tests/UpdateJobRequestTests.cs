using System.ComponentModel.DataAnnotations;
using Recruitment.Api.Dtos.Job;
using Recruitment.Api.Entities;

namespace Recruitment.Api.Tests;

// Unit tests thuần: chỉ kiểm tra DataAnnotations của DTO, không sử dụng database.
public class UpdateJobRequestTests
{
    private static UpdateJobRequest ValidRequest() => new()
    {
        Title = "Backend Developer",
        Description = "Develop backend services",
        Requirements = "C# and SQL",
        Location = "Da Nang",
        JobType = JobType.FULL_TIME,
        Experience = "2 years",
        NumberOfPositions = 2,
        SalaryMin = 1000,
        SalaryMax = 2500,
        Deadline = DateTime.UtcNow.AddDays(30),
        CategoryId = "CAT-01",
        SkillIds = new List<string> { "SKILL-01" }
    };

    private static List<ValidationResult> Validate(UpdateJobRequest request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            results,
            validateAllProperties: true);
        return results;
    }

    private static void SetRequiredString(UpdateJobRequest request, string name, string value)
    {
        switch (name)
        {
            case nameof(UpdateJobRequest.Title): request.Title = value; break;
            case nameof(UpdateJobRequest.Description): request.Description = value; break;
            case nameof(UpdateJobRequest.Requirements): request.Requirements = value; break;
            case nameof(UpdateJobRequest.Location): request.Location = value; break;
            case nameof(UpdateJobRequest.Experience): request.Experience = value; break;
            case nameof(UpdateJobRequest.CategoryId): request.CategoryId = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(name));
        }
    }

    private static void AssertFieldHasError(UpdateJobRequest request, string field)
    {
        Assert.Contains(Validate(request), error => error.MemberNames.Contains(field));
    }

    [Fact]
    public void ValidRequest_HasNoDataAnnotationErrors()
    {
        Assert.Empty(Validate(ValidRequest()));
    }

    // 6 cases: mỗi trường bắt buộc bị bỏ trống.
    [Theory]
    [InlineData(nameof(UpdateJobRequest.Title))]
    [InlineData(nameof(UpdateJobRequest.Description))]
    [InlineData(nameof(UpdateJobRequest.Requirements))]
    [InlineData(nameof(UpdateJobRequest.Location))]
    [InlineData(nameof(UpdateJobRequest.Experience))]
    [InlineData(nameof(UpdateJobRequest.CategoryId))]
    public void RequiredString_Empty_IsRejected(string field)
    {
        var request = ValidRequest();
        SetRequiredString(request, field, string.Empty);
        AssertFieldHasError(request, field);
    }

    // 6 cases: chuỗi chỉ gồm dấu cách cũng bị coi là trống.
    [Theory]
    [InlineData(nameof(UpdateJobRequest.Title))]
    [InlineData(nameof(UpdateJobRequest.Description))]
    [InlineData(nameof(UpdateJobRequest.Requirements))]
    [InlineData(nameof(UpdateJobRequest.Location))]
    [InlineData(nameof(UpdateJobRequest.Experience))]
    [InlineData(nameof(UpdateJobRequest.CategoryId))]
    public void RequiredString_Whitespace_IsRejected(string field)
    {
        var request = ValidRequest();
        SetRequiredString(request, field, "   ");
        AssertFieldHasError(request, field);
    }

    [Fact]
    public void JobType_Null_IsRejected()
    {
        var request = ValidRequest();
        request.JobType = null;
        AssertFieldHasError(request, nameof(UpdateJobRequest.JobType));
    }

    [Fact]
    public void JobType_UndefinedEnum_IsRejected()
    {
        var request = ValidRequest();
        request.JobType = (JobType)999;
        AssertFieldHasError(request, nameof(UpdateJobRequest.JobType));
    }

    // 3 cases: biên dưới của số vị trí.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void NumberOfPositions_ZeroOrNegative_IsRejected(int value)
    {
        var request = ValidRequest();
        request.NumberOfPositions = value;
        AssertFieldHasError(request, nameof(UpdateJobRequest.NumberOfPositions));
    }

    // 2 cases: lương tối thiểu không được âm.
    [Theory]
    [InlineData(-1)]
    [InlineData(-1000)]
    public void SalaryMin_Negative_IsRejected(int value)
    {
        var request = ValidRequest();
        request.SalaryMin = value;
        AssertFieldHasError(request, nameof(UpdateJobRequest.SalaryMin));
    }

    // 2 cases: lương tối đa không được âm.
    [Theory]
    [InlineData(-1)]
    [InlineData(-1000)]
    public void SalaryMax_Negative_IsRejected(int value)
    {
        var request = ValidRequest();
        request.SalaryMax = value;
        AssertFieldHasError(request, nameof(UpdateJobRequest.SalaryMax));
    }

    [Fact]
    public void SkillIds_EmptyList_IsRejected()
    {
        var request = ValidRequest();
        request.SkillIds = new List<string>();
        AssertFieldHasError(request, nameof(UpdateJobRequest.SkillIds));
    }

    // 5 cases: mọi JobType được định nghĩa đều hợp lệ.
    [Theory]
    [InlineData(JobType.FULL_TIME)]
    [InlineData(JobType.PART_TIME)]
    [InlineData(JobType.INTERNSHIP)]
    [InlineData(JobType.CONTRACT)]
    [InlineData(JobType.FREELANCE)]
    public void JobType_KnownEnum_IsAccepted(JobType type)
    {
        var request = ValidRequest();
        request.JobType = type;
        Assert.Empty(Validate(request));
    }

    [Fact]
    public void Salary_BothNull_IsAccepted()
    {
        var request = ValidRequest();
        request.SalaryMin = null;
        request.SalaryMax = null;
        Assert.Empty(Validate(request));
    }

    [Fact]
    public void Salary_OnlyMinSpecified_IsAccepted()
    {
        var request = ValidRequest();
        request.SalaryMax = null;
        Assert.Empty(Validate(request));
    }

    [Fact]
    public void Salary_OnlyMaxSpecified_IsAccepted()
    {
        var request = ValidRequest();
        request.SalaryMin = null;
        Assert.Empty(Validate(request));
    }

    [Fact]
    public void Salary_ZeroBounds_AreAccepted()
    {
        var request = ValidRequest();
        request.SalaryMin = 0;
        request.SalaryMax = 0;
        Assert.Empty(Validate(request));
    }

    [Fact]
    public void Salary_PositiveBounds_AreAccepted()
    {
        var request = ValidRequest();
        request.SalaryMin = 1200;
        request.SalaryMax = 2500;
        Assert.Empty(Validate(request));
    }

    [Fact]
    public void Skills_MultipleIds_AreAcceptedByDto()
    {
        var request = ValidRequest();
        request.SkillIds = new List<string> { "SKILL-01", "SKILL-02" };
        Assert.Empty(Validate(request));
    }

    [Fact]
    public void NumberOfPositions_One_IsAccepted()
    {
        var request = ValidRequest();
        request.NumberOfPositions = 1;
        Assert.Empty(Validate(request));
    }

    [Fact]
    public void NumberOfPositions_IntMaxValue_IsAccepted()
    {
        var request = ValidRequest();
        request.NumberOfPositions = int.MaxValue;
        Assert.Empty(Validate(request));
    }
}
