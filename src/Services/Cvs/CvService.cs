using System.IO.Compression;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Recruitment.Api.Data;
using Recruitment.Api.Dtos.Cv;
using Recruitment.Api.Entities;

namespace Recruitment.Api.Services.Cvs;

public sealed class CvService : ICvService
{
    // The story does not prescribe a limit. Keep it explicit and easy to revise.
    public const long MaxFileBytes = 5 * 1024 * 1024;
    private readonly AppDbContext _db;
    private readonly string _storageRoot;

    public CvService(AppDbContext db, IWebHostEnvironment environment)
    {
        _db = db;
        // NEVER store private CVs below wwwroot or serve this directory as static files.
        _storageRoot = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "PrivateFiles", "Cvs"));
    }

    public async Task<CvResponse> UploadAsync(string userId, IFormFile file, CancellationToken ct = default)
    {
        ValidateUser(userId);
        var validated = await ReadAndValidateFileAsync(file, ct);
        var cv = MakeCv(userId, validated);

        await PersistFileAndRecordAsync(validated.Bytes, cv, null, ct);
        return ToResponse(cv);
    }

    public async Task<IReadOnlyList<CvResponse>> ListAsync(string userId, CancellationToken ct = default)
    {
        ValidateUser(userId);
        var cvs = await _db.CandidateCvs.AsNoTracking()
            .Where(x => x.UserId == userId && x.DeletedAtUtc == null)
            .OrderByDescending(x => x.UploadedAtUtc)
            .ThenByDescending(x => x.CvId)
            .ToListAsync(ct);
        return cvs.Select(ToResponse).ToList();
    }

    public async Task<CvDownload?> GetDownloadAsync(string userId, string cvId, CancellationToken ct = default)
    {
        ValidateUser(userId);
        var cv = await FindActiveOwnedCvAsync(userId, cvId, ct);
        if (cv is null) return null;
        var filePath = StoragePath(cv.StorageKey);
        if (!File.Exists(filePath))
            throw new IOException("CV metadata exists but its file is unavailable.");
        return new CvDownload(filePath, cv.ContentType, cv.OriginalFileName);
    }

    public async Task<CvResponse?> ReplaceAsync(string userId, string cvId, IFormFile file, CancellationToken ct = default)
    {
        ValidateUser(userId);
        var oldCv = await FindActiveOwnedCvAsync(userId, cvId, ct);
        if (oldCv is null) return null;

        var validated = await ReadAndValidateFileAsync(file, ct);
        var replacement = MakeCv(userId, validated);
        oldCv.DeletedAtUtc = DateTime.UtcNow;
        oldCv.ReplacedByCvId = replacement.CvId;

        // One SaveChanges covers the new row and the old row's tombstone atomically.
        await PersistFileAndRecordAsync(validated.Bytes, replacement, oldCv, ct);
        return ToResponse(replacement);
    }

    public async Task<bool> DeleteAsync(string userId, string cvId, CancellationToken ct = default)
    {
        ValidateUser(userId);
        var cv = await FindActiveOwnedCvAsync(userId, cvId, ct);
        if (cv is null) return false;

        // Soft delete only: old Applications may need the exact original CV.
        cv.DeletedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<CandidateCv?> FindActiveOwnedCvAsync(string userId, string cvId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cvId)) return null;
        return await _db.CandidateCvs.SingleOrDefaultAsync(
            x => x.CvId == cvId && x.UserId == userId && x.DeletedAtUtc == null, ct);
    }

    private static CandidateCv MakeCv(string userId, ValidatedFile file) => new()
    {
        UserId = userId,
        OriginalFileName = file.FileName,
        StorageKey = Guid.NewGuid().ToString("N") + file.Extension,
        ContentType = file.ContentType,
        FileSizeBytes = file.Bytes.LongLength,
        UploadedAtUtc = DateTime.UtcNow
    };

    private async Task PersistFileAndRecordAsync(
        byte[] bytes, CandidateCv newCv, CandidateCv? priorCv, CancellationToken ct)
    {
        Directory.CreateDirectory(_storageRoot);
        var path = StoragePath(newCv.StorageKey);
        bool created = false;
        try
        {
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 81920, FileOptions.Asynchronous))
            {
                created = true;
                await output.WriteAsync(bytes, ct);
                await output.FlushAsync(ct);
            }

            _db.CandidateCvs.Add(newCv);
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            // Avoid orphaned files if disk IO / EF SaveChanges fails.
            if (created)
            {
                try { File.Delete(path); }
                catch (IOException) { /* error remains visible; operator can clean up */ }
                catch (UnauthorizedAccessException) { /* do not obscure original exception */ }
            }
            // Revert tracked state for test/retry after a failed replacement.
            _db.Entry(newCv).State = EntityState.Detached;
            if (priorCv is not null)
            {
                priorCv.DeletedAtUtc = null;
                priorCv.ReplacedByCvId = null;
                _db.Entry(priorCv).State = EntityState.Unchanged;
            }
            throw;
        }
    }

    private string StoragePath(string storageKey)
    {
        // Only internal randomly generated names can form storage paths.
        var extension = Path.GetExtension(storageKey);
        var name = Path.GetFileNameWithoutExtension(storageKey);
        if (Path.GetFileName(storageKey) != storageKey ||
            (extension != ".pdf" && extension != ".docx") ||
            name.Length != 32 || !name.All(Uri.IsHexDigit))
        {
            throw new IOException("Invalid internal CV storage key.");
        }
        return Path.Combine(_storageRoot, storageKey);
    }

    private static async Task<ValidatedFile> ReadAndValidateFileAsync(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length <= 0)
            throw new CvValidationException("Vui lòng chọn CV không rỗng.");
        if (file.Length > MaxFileBytes)
            throw new CvValidationException("CV không được vượt quá 5 MB.");

        // Browser-supplied filename and ContentType are not trusted.
        var safeName = Path.GetFileName(file.FileName.Replace('\\', '/')).Trim();
        var ext = Path.GetExtension(safeName).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(safeName) || safeName.Length > 255 ||
            (ext != ".pdf" && ext != ".docx"))
            throw new CvValidationException("Chỉ hỗ trợ CV định dạng PDF hoặc DOCX.");

        await using var stream = file.OpenReadStream();
        await using var buffer = new MemoryStream(capacity: (int)file.Length);
        byte[] chunk = new byte[81920];
        int n;
        while ((n = await stream.ReadAsync(chunk.AsMemory(), ct)) > 0)
        {
            if (buffer.Length + n > MaxFileBytes)
                throw new CvValidationException("CV không được vượt quá 5 MB.");
            await buffer.WriteAsync(chunk.AsMemory(0, n), ct);
        }
        if (buffer.Length != file.Length)
            throw new CvValidationException("Kích thước CV không hợp lệ.");

        byte[] data = buffer.ToArray();
        if (ext == ".pdf")
        {
            if (data.Length < 5 || data[0] != '%' || data[1] != 'P' ||
                data[2] != 'D' || data[3] != 'F' || data[4] != '-')
                throw new CvValidationException("Nội dung tệp không phải PDF hợp lệ.");
        }
        else
        {
            try
            {
                using var zipStream = new MemoryStream(data, writable: false);
                using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
                if (zip.GetEntry("[Content_Types].xml") is null ||
                    zip.GetEntry("word/document.xml") is null)
                    throw new CvValidationException("Nội dung tệp không phải DOCX hợp lệ.");
            }
            catch (InvalidDataException)
            {
                throw new CvValidationException("Nội dung tệp không phải DOCX hợp lệ.");
            }
        }

        return new ValidatedFile(safeName, ext,
            ext == ".pdf" ? "application/pdf" :
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document", data);
    }

    private static void ValidateUser(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new UnauthorizedAccessException("Missing authenticated user ID.");
    }

    private static CvResponse ToResponse(CandidateCv x) => new(
        x.CvId, x.OriginalFileName, x.ContentType, x.FileSizeBytes, x.UploadedAtUtc);

    private sealed record ValidatedFile(string FileName, string Extension, string ContentType, byte[] Bytes);
}
