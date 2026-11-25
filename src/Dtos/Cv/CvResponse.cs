namespace Recruitment.Api.Dtos.Cv;

public sealed record CvResponse(
    string CvId,
    string OriginalFileName,
    string ContentType,
    long FileSizeBytes,
    DateTime UploadedAtUtc);

public sealed record CvDownload(
    string Path,
    string ContentType,
    string OriginalFileName);
