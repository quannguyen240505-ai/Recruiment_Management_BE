using Microsoft.EntityFrameworkCore;
using Recruitment.Api.Data;
using Recruitment.Api.Dtos;
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
    public async Task<PagedResult<JobSummaryResponse>> GetMyJobsAsync(string recruiterId, JobStatus? status, int page, int pageSize)
    {
        // 1. Kiểm tra tham số phân trang (Quốc Feedback 1)
        if (page <= 0)
            throw new ArgumentException("Page phải lớn hơn 0.");
        if (pageSize <= 0 || pageSize > 100)
            throw new ArgumentException("PageSize phải nằm trong khoảng từ 1 đến 100.");

        // 2. Kiểm tra tài khoản (Quốc Feedback 2)
        var recruiter = await _context.Users.FirstOrDefaultAsync(u => u.UserId == recruiterId);
        if (recruiter == null ||
            recruiter.RoleId != "RECRUITER" ||
            recruiter.AccountStatus != AccountStatus.ACTIVE ||
            recruiter.DeletedAtUtc != null)
        {
            throw new UnauthorizedAccessException("Tài khoản không hợp lệ, đã bị khóa hoặc không có quyền.");
        }
        long skipLong = ((long)page - 1) * pageSize;

        if (skipLong > int.MaxValue)
            throw new ArgumentException("Page vượt quá giới hạn phân trang.");

        int skip = (int)skipLong;

        // 4. Query
        var query = _context.Jobs
            .Include(j => j.Company)
            .Where(j => j.RecruiterId == recruiterId && j.DeletedAtUtc == null)
            .AsQueryable();

        if (status.HasValue)
            query = query.Where(j => j.Status == status.Value);

        var totalCount = await query.CountAsync();

        var jobs = await query
            .OrderByDescending(j => j.CreatedAt)
            .Skip(skip)
            .Take(pageSize)
            .Select(j => new JobSummaryResponse
            {
                JobId = j.JobId,
                Title = j.Title,
                Status = j.Status.ToString(),
                CompanyName = j.Company.Name,
                Deadline = j.Deadline,
                CreatedAt = j.CreatedAt
            })
            .ToListAsync();

        return new PagedResult<JobSummaryResponse>
        {
            Items = jobs,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<SearchJobsResponse> SearchJobsAsync(
        SearchJobsRequest request)
    {
        // 1. Kiểm tra tham số
        if (request.Page < 0)
            throw new ArgumentException("Page phải >= 0.");

        if (request.Size < 1 || request.Size > 100)
            throw new ArgumentException("Size phải từ 1 đến 100.");

        if (request.SalaryMin < 0)
            throw new ArgumentException("SalaryMin không được âm.");

        if (request.JobType.HasValue &&
            !Enum.IsDefined(typeof(JobType), request.JobType.Value))
            throw new ArgumentException("JobType không hợp lệ.");

        long skipLong = (long)request.Page * request.Size;

        if (skipLong > int.MaxValue)
            throw new ArgumentException("Page vượt giới hạn phân trang.");

        // 2. Chỉ lấy Job công khai, còn hạn
        var tomorrowUtc = DateTime.UtcNow.Date.AddDays(1);

        var query = _context.Jobs
            .AsNoTracking()
            .Where(j =>
                j.Status == JobStatus.OPEN &&
                j.DeletedAtUtc == null &&
                j.Deadline >= tomorrowUtc);

        // 3. Tìm theo tiêu đề hoặc tên công ty
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim().ToLower();

            query = query.Where(j =>
                j.Title.ToLower().Contains(keyword) ||
                j.Company.Name.ToLower().Contains(keyword));
        }

        // 4. Bộ lọc địa điểm
        if (!string.IsNullOrWhiteSpace(request.Location))
        {
            var location = request.Location.Trim().ToLower();

            query = query.Where(j =>
                j.Location.ToLower().Contains(location));
        }

        // 5. Loại công việc
        if (request.JobType.HasValue)
        {
            var type = request.JobType.Value;
            query = query.Where(j => j.JobType == type);
        }

        // 6. Kinh nghiệm
        if (!string.IsNullOrWhiteSpace(request.Experience))
        {
            var experience = request.Experience.Trim().ToLower();

            query = query.Where(j =>
                j.Experience.ToLower().Contains(experience));
        }

        // 7. Lương tối thiểu kỳ vọng
        if (request.SalaryMin.HasValue)
        {
            var min = request.SalaryMin.Value;

            query = query.Where(j =>
                j.SalaryMax.HasValue &&
                j.SalaryMax.Value >= min);
        }

        // 8. Kỹ năng
        if (!string.IsNullOrWhiteSpace(request.SkillId))
        {
            var skillId = request.SkillId.Trim();

            query = query.Where(j =>
                j.JobSkills.Any(js => js.SkillId == skillId));
        }

        // 9. Danh mục
        if (!string.IsNullOrWhiteSpace(request.CategoryId))
        {
            var categoryId = request.CategoryId.Trim();

            query = query.Where(j => j.CategoryId == categoryId);
        }

        // 10. Công ty
        if (!string.IsNullOrWhiteSpace(request.CompanyId))
        {
            var companyId = request.CompanyId.Trim();

            query = query.Where(j => j.CompanyId == companyId);
        }

        // 11. Đếm trước khi phân trang
        var totalItems = await query.CountAsync();

        // 12. Phân trang từ page = 0
        var rows = await query
            .OrderByDescending(j => j.CreatedAt)
            .ThenBy(j => j.JobId)
            .Skip((int)skipLong)
            .Take(request.Size)
            .Select(j => new
            {
                j.JobId,
                j.Title,
                j.CompanyId,
                CompanyName = j.Company.Name,
                j.CategoryId,
                j.Location,
                j.JobType,
                j.Experience,
                j.SalaryMin,
                j.SalaryMax,
                j.Deadline,
                j.CreatedAt
            })
            .ToListAsync();

        var items = rows.Select(j => new SearchJobItemResponse
        {
            JobId = j.JobId,
            Title = j.Title,
            CompanyId = j.CompanyId,
            CompanyName = j.CompanyName,
            CategoryId = j.CategoryId,
            Location = j.Location,
            JobType = j.JobType.ToString(),
            Experience = j.Experience,
            SalaryMin = j.SalaryMin,
            SalaryMax = j.SalaryMax,
            Deadline = j.Deadline,
            CreatedAt = j.CreatedAt
        }).ToList();

        return new SearchJobsResponse
        {
            Items = items,
            Page = request.Page,
            Size = request.Size,
            TotalItems = totalItems
        };
    }
}
