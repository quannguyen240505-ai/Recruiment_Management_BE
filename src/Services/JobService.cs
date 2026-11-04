using Microsoft.EntityFrameworkCore;
using Recruitment.Api.Data;
using Recruitment.Api.Dtos;
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
        // 1. Kiểm tra Recruiter
        var recruiter = await _context.Users.FirstOrDefaultAsync(u => u.UserId == recruiterId);
        if (recruiter == null || recruiter.RoleId != "RECRUITER")
            throw new UnauthorizedAccessException("Chỉ Recruiter mới có quyền tạo Job.");

        if (string.IsNullOrEmpty(recruiter.CompanyId))
            throw new InvalidOperationException("Tài khoản Recruiter chưa liên kết với Công ty hợp lệ.");

        // 2. Validate Deadline
        if (request.Deadline.Date <= DateTime.UtcNow.Date)
            throw new ArgumentException("Deadline phải ở tương lai (lớn hơn ngày hiện tại).");

        // 3. Validate Lương
        if (request.SalaryMin.HasValue && request.SalaryMax.HasValue)
        {
            if (request.SalaryMax < request.SalaryMin)
                throw new ArgumentException("Lương tối đa (SalaryMax) phải lớn hơn hoặc bằng Lương tối thiểu (SalaryMin).");
        }

        // 4. Validate Category & Skills
        var categoryExists = await _context.Categories.AnyAsync(c => c.CategoryId == request.CategoryId);
        if (!categoryExists) throw new ArgumentException("Category không hợp lệ.");

        var validSkillCount = await _context.Skills.CountAsync(s => request.SkillIds.Contains(s.SkillId));
        if (validSkillCount != request.SkillIds.Count)
            throw new ArgumentException("Có Skill không hợp lệ hoặc không tồn tại trong hệ thống.");

        // 5. Tạo Job mới
        var job = new Job
        {
            JobId = Guid.NewGuid().ToString(),
            Title = request.Title,
            Description = request.Description,
            Requirements = request.Requirements,
            Location = request.Location,
            JobType = request.JobType,
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

        // 6. Lưu JobSkills
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
}
