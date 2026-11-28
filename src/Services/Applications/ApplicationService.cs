
using Microsoft.EntityFrameworkCore;
using Recruitment.Api.Data;
using Recruitment.Api.Dtos.Application;
using Recruitment.Api.Entities;

namespace Recruitment.Api.Services.Applications;

public class ApplicationService
{
    private readonly AppDbContext _context;

    public ApplicationService(AppDbContext context)
    {
        _context = context;
    }

    // Candidate xem danh sách Application của mình
    public async Task<MyApplicationsResponse> GetMyApplicationsAsync(
        string candidateId,
        int page = 1,
        int pageSize = 10)
    {
        if (string.IsNullOrWhiteSpace(candidateId))
            throw new ArgumentException("CandidateId không hợp lệ.");

        if (page < 1 || pageSize < 1 || pageSize > 100)
            throw new ArgumentException("Tham số phân trang không hợp lệ.");

        var skip = ((long)page - 1) * pageSize;

        if (skip > int.MaxValue)
            throw new ArgumentException("Page vượt giới hạn.");

        var query = _context.Applications
            .AsNoTracking()
            .Where(a => a.CandidateId == candidateId);

        var totalCount = await query.CountAsync();

        var applications = await query
            .Include(a => a.Job)
                .ThenInclude(j => j.Company)
            .OrderByDescending(a => a.AppliedAtUtc)
            .ThenBy(a => a.ApplicationId)
            .Skip((int)skip)
            .Take(pageSize)
            .ToListAsync();

        return new MyApplicationsResponse
        {
            Items = applications.Select(MapToResponse).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    // Candidate xem chi tiết Application của chính mình
    public async Task<MyApplicationDetailResponse?> GetMyApplicationDetailAsync(
        string candidateId,
        string applicationId)
    {
        if (string.IsNullOrWhiteSpace(candidateId) ||
            string.IsNullOrWhiteSpace(applicationId))
            return null;

        var application = await _context.Applications
            .AsNoTracking()
            .Include(a => a.Job)
                .ThenInclude(j => j.Company)
            .FirstOrDefaultAsync(a =>
                a.ApplicationId == applicationId &&
                a.CandidateId == candidateId);

        if (application == null)
            return null;

        var summary = MapToResponse(application);

        return new MyApplicationDetailResponse
        {
            ApplicationId = summary.ApplicationId,
            JobId = summary.JobId,
            JobTitle = summary.JobTitle,
            CompanyName = summary.CompanyName,
            CvId = summary.CvId,
            Status = summary.Status,
            AppliedAtUtc = summary.AppliedAtUtc,
            CoverLetter = application.CoverLetter
        };
    }

    private static MyApplicationResponse MapToResponse(
        Application application)
    {
        return new MyApplicationResponse
        {
            ApplicationId = application.ApplicationId,
            JobId = application.JobId,
            JobTitle = application.Job.Title,
            CompanyName = application.Job.Company.Name,
            CvId = application.CvId,
            Status = application.Status.ToString(),
            AppliedAtUtc = application.AppliedAtUtc
        };
    }
}
