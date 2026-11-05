using Microsoft.EntityFrameworkCore;
using Recruitment.Api.Data;
using Recruitment.Api.Dtos.Job;
using Recruitment.Api.Entities;

namespace Recruitment.Api.Services.Jobs;

public class JobService : IJobService
{
    private readonly AppDbContext _context;

    public JobService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<JobResponse> CreateJobAsync(CreateJobRequest request, string recruiterId)
    {
        // 1. Kiểm tra Recruiter (Đã sửa: check ACTIVE và DeletedAtUtc)
        var recruiter = await _context.Users.FirstOrDefaultAsync(u => u.UserId == recruiterId);

        if (recruiter == null ||
            recruiter.RoleId != "RECRUITER" ||
            recruiter.AccountStatus != AccountStatus.ACTIVE ||
            recruiter.DeletedAtUtc != null)
        {
            throw new UnauthorizedAccessException("Tài khoản Recruiter không tồn tại, đã bị khóa hoặc không có quyền.");
        }

        if (string.IsNullOrEmpty(recruiter.CompanyId))
            throw new InvalidOperationException("Tài khoản Recruiter chưa liên kết với Công ty hợp lệ.");

        // 2. Kiểm tra Company có thực sự tồn tại trong DB không (Đã sửa)
        var companyExists = await _context.Companies.AnyAsync(c => c.CompanyId == recruiter.CompanyId);
        if (!companyExists)
            throw new InvalidOperationException("Công ty liên kết với tài khoản không tồn tại trong hệ thống.");

        // 3. Validate Deadline
        if (request.Deadline.Date <= DateTime.UtcNow.Date)
            throw new ArgumentException("Deadline phải ở tương lai (lớn hơn ngày hiện tại).");

        // 4. Validate Lương
        if (request.SalaryMin.HasValue && request.SalaryMax.HasValue)
        {
            if (request.SalaryMax < request.SalaryMin)
                throw new ArgumentException("Lương tối đa (SalaryMax) phải lớn hơn hoặc bằng Lương tối thiểu (SalaryMin).");
        }

        // 5. Validate SkillIds bị null (Đã sửa)
        if (request.SkillIds == null || request.SkillIds.Count == 0)
            throw new ArgumentException("Phải chọn ít nhất 1 kỹ năng (Skill).");

        // 6. Validate Category & Skills có tồn tại không
        var categoryExists = await _context.Categories.AnyAsync(c => c.CategoryId == request.CategoryId);
        if (!categoryExists) throw new ArgumentException("Category không hợp lệ.");

        var validSkillCount = await _context.Skills.CountAsync(s => request.SkillIds.Contains(s.SkillId));
        if (validSkillCount != request.SkillIds.Count)
            throw new ArgumentException("Có Skill không hợp lệ hoặc không tồn tại trong hệ thống.");

        // 7. Tạo Job mới
        var job = new Job
        {
            JobId = Guid.NewGuid().ToString(),
            Title = request.Title,
            Description = request.Description,
            Requirements = request.Requirements,
            Location = request.Location,
            JobType = request.JobType!.Value, // Đã sửa vì JobType trong DTO giờ là nullable
            Experience = request.Experience,
            NumberOfPositions = request.NumberOfPositions,
            SalaryMin = request.SalaryMin,
            SalaryMax = request.SalaryMax,
            Deadline = request.Deadline,
            Status = JobStatus.OPEN,
            CreatedAt = DateTime.UtcNow,
            CategoryId = request.CategoryId,
            RecruiterId = recruiterId,
            CompanyId = recruiter.CompanyId
        };

        _context.Jobs.Add(job);

        // 8. Lưu JobSkills
        foreach (var skillId in request.SkillIds)
        {
            _context.JobSkills.Add(new JobSkill { JobId = job.JobId, SkillId = skillId });
        }

        await _context.SaveChangesAsync();

        return new JobResponse
        {
            JobId = job.JobId,
            Title = job.Title,
            Status = job.Status.ToString(),
            CreatedAt = job.CreatedAt
        };
    }

    public async Task<JobDetailResponse?> GetJobDetailAsync(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return null;

        var tomorrowUtc = DateTime.UtcNow.Date.AddDays(1);

        var job = await _context.Jobs
            .AsNoTracking()
            .Include(j => j.Company)
            .Include(j => j.JobSkills)
                .ThenInclude(js => js.Skill)
            .FirstOrDefaultAsync(j =>
                j.JobId == jobId &&
                j.Status == JobStatus.OPEN &&
                j.DeletedAtUtc == null &&
                j.Deadline >= tomorrowUtc);

        if (job == null)
            return null;

        return new JobDetailResponse
        {
            JobId = job.JobId,
            Title = job.Title,
            Description = job.Description,
            Requirements = job.Requirements,
            Location = job.Location,
            JobType = job.JobType.ToString(),
            Experience = job.Experience,
            NumberOfPositions = job.NumberOfPositions,
            SalaryMin = job.SalaryMin,
            SalaryMax = job.SalaryMax,
            Deadline = job.Deadline,
            Status = job.Status.ToString(),
            CreatedAt = job.CreatedAt,

            Company = new JobCompanyResponse
            {
                CompanyId = job.Company.CompanyId,
                Name = job.Company.Name,
                Website = job.Company.Website,
                Address = job.Company.Address,
                Description = job.Company.Description
            },

            Skills = job.JobSkills
                .Select(js => new JobSkillResponse
                {
                    SkillId = js.SkillId,
                    Name = js.Skill.Name
                })
                .OrderBy(s => s.Name)
                .ToList()
        };
    }

}
