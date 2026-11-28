
using Microsoft.EntityFrameworkCore;
using Recruitment.Api.Data;
using Recruitment.Api.Dtos.Job;
using Recruitment.Api.Entities;

namespace Recruitment.Api.Services.Jobs;

public class CloseJobService
{
    private readonly AppDbContext _context;

    public CloseJobService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<CloseJobResponse?> CloseJobAsync(
        string recruiterId,
        string jobId)
    {
        // 1. Kiểm tra người dùng
        if (string.IsNullOrWhiteSpace(recruiterId))
            throw new UnauthorizedAccessException(
                "Người dùng chưa đăng nhập.");

        if (string.IsNullOrWhiteSpace(jobId))
            throw new ArgumentException("JobId không hợp lệ.");

        var validRecruiter = await _context.Users
            .AsNoTracking()
            .AnyAsync(u =>
                u.UserId == recruiterId &&
                u.RoleId == "RECRUITER" &&
                u.AccountStatus == AccountStatus.ACTIVE &&
                u.DeletedAtUtc == null);

        if (!validRecruiter)
            throw new UnauthorizedAccessException(
                "Tài khoản Recruiter không hợp lệ.");

        // 2. Chỉ tìm Job thuộc Recruiter đang đăng nhập
        var job = await _context.Jobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j =>
                j.JobId == jobId &&
                j.RecruiterId == recruiterId &&
                j.DeletedAtUtc == null);

        if (job == null)
            return null;

        // 3. Job đã đóng: trả thành công, không sửa dữ liệu
        if (job.Status == JobStatus.CLOSED)
        {
            return new CloseJobResponse
            {
                JobId = job.JobId,
                Status = "CLOSED",
                AlreadyClosed = true
            };
        }

        // 4. Chỉ cho đóng Job đang OPEN
        if (job.Status != JobStatus.OPEN)
            throw new InvalidOperationException(
                "Chỉ có thể đóng Job đang OPEN.");

        // 5. Cập nhật có điều kiện để tránh đóng đồng thời
        var affectedRows = await _context.Jobs
            .Where(j =>
                j.JobId == jobId &&
                j.RecruiterId == recruiterId &&
                j.DeletedAtUtc == null &&
                j.Status == JobStatus.OPEN)
            .ExecuteUpdateAsync(setters =>
                setters.SetProperty(
                    j => j.Status,
                    JobStatus.CLOSED));

        if (affectedRows == 1)
        {
            return new CloseJobResponse
            {
                JobId = jobId,
                Status = "CLOSED",
                AlreadyClosed = false
            };
        }

        // 6. Kiểm tra nếu có request khác vừa đóng Job
        var currentStatus = await _context.Jobs
            .AsNoTracking()
            .Where(j =>
                j.JobId == jobId &&
                j.RecruiterId == recruiterId &&
                j.DeletedAtUtc == null)
            .Select(j => (JobStatus?)j.Status)
            .FirstOrDefaultAsync();

        if (currentStatus == null)
            return null;

        if (currentStatus == JobStatus.CLOSED)
        {
            return new CloseJobResponse
            {
                JobId = jobId,
                Status = "CLOSED",
                AlreadyClosed = true
            };
        }

        throw new InvalidOperationException(
            "Trạng thái Job đã thay đổi. Vui lòng thử lại.");
    }
}
