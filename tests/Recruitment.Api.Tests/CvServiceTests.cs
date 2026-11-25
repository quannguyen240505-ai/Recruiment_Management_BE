using System.Text;
using System.IO.Compression;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Recruitment.Api.Data;
using Recruitment.Api.Entities;
using Recruitment.Api.Services.Cvs;

namespace Recruitment.Api.Tests;

public sealed class CvServiceTests
{
    [Fact]
    public async Task Upload_ValidPdf_AndList_ByOwner()
    {
        await using var ctx = await TestContext.CreateAsync();
        var cv = await ctx.Service.UploadAsync("candidate-a", Pdf("cv.pdf"));
        var list = await ctx.Service.ListAsync("candidate-a");
        Assert.Single(list);
        Assert.Equal(cv.CvId, list[0].CvId);
        Assert.Equal("cv.pdf", list[0].OriginalFileName);
        Assert.Empty(await ctx.Service.ListAsync("candidate-b"));
    }

    [Fact]
    public async Task Upload_InvalidExtension_DoesNotPersist()
    {
        await using var ctx = await TestContext.CreateAsync();
        var file = File("fake.exe", Encoding.UTF8.GetBytes("not a CV"));
        await Assert.ThrowsAsync<CvValidationException>(
            () => ctx.Service.UploadAsync("candidate-a", file));
        Assert.Empty(await ctx.Service.ListAsync("candidate-a"));
    }

    [Fact]
    public async Task Upload_FakePdfRejected()
    {
        await using var ctx = await TestContext.CreateAsync();
        await Assert.ThrowsAsync<CvValidationException>(
            () => ctx.Service.UploadAsync("candidate-a", File("fake.pdf", Encoding.UTF8.GetBytes("fake"))));
        Assert.Empty(await ctx.Service.ListAsync("candidate-a"));
    }

    [Fact]
    public async Task Upload_EmptyFileRejected()
    {
        await using var ctx = await TestContext.CreateAsync();
        await Assert.ThrowsAsync<CvValidationException>(
            () => ctx.Service.UploadAsync("candidate-a", File("empty.pdf", Array.Empty<byte>())));
    }


    [Fact]
    public async Task Upload_ValidDocx_Accepted()
    {
        await using var ctx = await TestContext.CreateAsync();
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("[Content_Types].xml").Open()))
                writer.Write("<Types/>");
            using (var writer = new StreamWriter(archive.CreateEntry("word/document.xml").Open()))
                writer.Write("<w:document/>");
        }
        var result = await ctx.Service.UploadAsync("candidate-a", File("cv.docx", buffer.ToArray()));
        Assert.Equal("application/vnd.openxmlformats-officedocument.wordprocessingml.document", result.ContentType);
    }

    [Fact]
    public async Task Upload_OverFiveMiB_IsRejected()
    {
        await using var ctx = await TestContext.CreateAsync();
        var data = new byte[(int)CvService.MaxFileBytes + 1];
        data[0] = (byte)'%'; data[1] = (byte)'P'; data[2] = (byte)'D'; data[3] = (byte)'F'; data[4] = (byte)'-';
        await Assert.ThrowsAsync<CvValidationException>(
            () => ctx.Service.UploadAsync("candidate-a", File("too-large.pdf", data)));
        Assert.Empty(await ctx.Service.ListAsync("candidate-a"));
    }

    [Fact]
    public async Task UserCannotDownloadReplaceOrDeleteAnotherUsersCv()
    {
        await using var ctx = await TestContext.CreateAsync();
        var uploaded = await ctx.Service.UploadAsync("candidate-a", Pdf("private.pdf"));
        Assert.Null(await ctx.Service.GetDownloadAsync("candidate-b", uploaded.CvId));
        Assert.Null(await ctx.Service.ReplaceAsync("candidate-b", uploaded.CvId, Pdf("new.pdf")));
        Assert.False(await ctx.Service.DeleteAsync("candidate-b", uploaded.CvId));
        Assert.Single(await ctx.Service.ListAsync("candidate-a"));
    }

    [Fact]
    public async Task Replace_LeavesOriginalFileAndOnlyListsNewRecord()
    {
        await using var ctx = await TestContext.CreateAsync();
        var old = await ctx.Service.UploadAsync("candidate-a", Pdf("old.pdf"));
        var oldPath = (await ctx.Service.GetDownloadAsync("candidate-a", old.CvId))!.Path;
        var newer = await ctx.Service.ReplaceAsync("candidate-a", old.CvId, Pdf("new.pdf"));

        Assert.NotNull(newer);
        Assert.NotEqual(old.CvId, newer.CvId);
        Assert.True(System.IO.File.Exists(oldPath));
        Assert.Null(await ctx.Service.GetDownloadAsync("candidate-a", old.CvId));
        Assert.Equal(newer.CvId, Assert.Single(await ctx.Service.ListAsync("candidate-a")).CvId);

        var originalRow = await ctx.Db.CandidateCvs.SingleAsync(x => x.CvId == old.CvId);
        Assert.NotNull(originalRow.DeletedAtUtc);
        Assert.Equal(newer.CvId, originalRow.ReplacedByCvId);
    }

    [Fact]
    public async Task Delete_SoftDeletesRowButKeepsPhysicalFile()
    {
        await using var ctx = await TestContext.CreateAsync();
        var cv = await ctx.Service.UploadAsync("candidate-a", Pdf("cv.pdf"));
        var path = (await ctx.Service.GetDownloadAsync("candidate-a", cv.CvId))!.Path;

        Assert.True(await ctx.Service.DeleteAsync("candidate-a", cv.CvId));
        Assert.False(await ctx.Service.DeleteAsync("candidate-a", cv.CvId));
        Assert.Empty(await ctx.Service.ListAsync("candidate-a"));
        Assert.Null(await ctx.Service.GetDownloadAsync("candidate-a", cv.CvId));
        Assert.True(System.IO.File.Exists(path));
        Assert.NotNull((await ctx.Db.CandidateCvs.SingleAsync(x => x.CvId == cv.CvId)).DeletedAtUtc);
    }

    private static IFormFile Pdf(string name) =>
        File(name, Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<<>>\nendobj\n%%EOF"));

    private static IFormFile File(string name, byte[] bytes) => new FormFile(
        new MemoryStream(bytes), 0, bytes.Length, "file", name)
    {
        Headers = new HeaderDictionary(),
        ContentType = "application/octet-stream"
    };

    private sealed class TestContext : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly string _root;
        public AppDbContext Db { get; }
        public CvService Service { get; }

        private TestContext(SqliteConnection connection, AppDbContext db,
            CvService service, string root)
        {
            _connection = connection;
            Db = db;
            Service = service;
            _root = root;
        }

        public static async Task<TestContext> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();

            db.Users.AddRange(
                new User { UserId = "candidate-a", RoleId = "CANDIDATE", Username = "candidate-a",
                    Email = "a@example.test", FullName = "Candidate A" },
                new User { UserId = "candidate-b", RoleId = "CANDIDATE", Username = "candidate-b",
                    Email = "b@example.test", FullName = "Candidate B" });
            await db.SaveChangesAsync();

            var root = Path.Combine(Path.GetTempPath(), "recruitment-cv-test", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new TestContext(connection, db,
                new CvService(db, new TestEnvironment(root)), root);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
            try { Directory.Delete(_root, recursive: true); }
            catch (DirectoryNotFoundException) { }
        }
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public TestEnvironment(string root)
        {
            ContentRootPath = root;
            ContentRootFileProvider = new PhysicalFileProvider(root);
            WebRootPath = Path.Combine(root, "wwwroot");
            Directory.CreateDirectory(WebRootPath);
            WebRootFileProvider = new PhysicalFileProvider(WebRootPath);
        }

        public string ApplicationName { get; set; } = "Recruitment.Api";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
        public string WebRootPath { get; set; }
        public IFileProvider WebRootFileProvider { get; set; }
    }
}
