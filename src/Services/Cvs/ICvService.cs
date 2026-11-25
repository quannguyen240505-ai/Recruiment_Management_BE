using Microsoft.AspNetCore.Http;
using Recruitment.Api.Dtos.Cv;

namespace Recruitment.Api.Services.Cvs;

public interface ICvService
{
    Task<CvResponse> UploadAsync(string userId, IFormFile file, CancellationToken ct = default);
    Task<IReadOnlyList<CvResponse>> ListAsync(string userId, CancellationToken ct = default);
    Task<CvDownload?> GetDownloadAsync(string userId, string cvId, CancellationToken ct = default);
    Task<CvResponse?> ReplaceAsync(string userId, string cvId, IFormFile file, CancellationToken ct = default);
    Task<bool> DeleteAsync(string userId, string cvId, CancellationToken ct = default);
}

public sealed class CvValidationException : Exception
{
    public CvValidationException(string message) : base(message) { }
}
