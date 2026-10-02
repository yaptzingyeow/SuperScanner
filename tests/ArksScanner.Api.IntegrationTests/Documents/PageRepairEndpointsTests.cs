using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using ArksScanner.Api.Auth;
using ArksScanner.Application.Abstractions;
using ArksScanner.Application.Ocr;
using ArksScanner.Domain.Documents;
using ArksScanner.Domain.Ocr;
using ArksScanner.Domain.TextEditing;
using ArksScanner.Infrastructure.Persistence;
using ArksScanner.Infrastructure.Processing;
using Testcontainers.PostgreSql;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf;

namespace ArksScanner.Api.IntegrationTests.Documents;

public sealed class PageRepairEndpointsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly MemoryStore store = new();
    private WebApplicationFactory<Program> factory = null!;
    private Guid documentId, pageId;
    private string Url => $"/api/documents/{documentId}/pages/{pageId}/repair";

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        var connection = postgres.GetConnectionString();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Audit:SigningKeyBase64", Convert.ToBase64String(new byte[32]));
            builder.UseSetting("Audit:SigningKeyId", "test");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRequestIdentityVerifier>();
                services.AddSingleton<IRequestIdentityVerifier, Identity>();
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<AppDbContext>();
                services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connection));
                services.RemoveAll<IObjectStore>();
                services.AddSingleton<IObjectStore>(store);
            });
        });
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        var document = Document.Create(Guid.NewGuid(), "owner", "Cleanup test", DateTimeOffset.UtcNow);
        var page = document.AddPage(Guid.NewGuid(), 20, DateTimeOffset.UtcNow);
        page.MarkImportReady("originals/source.jpg", "image/jpeg");
        page.SetPreview("previews/source.jpg", "thumbnails/source.jpg");
        page.MarkReady(); document.MarkReady(DateTimeOffset.UtcNow);
        documentId = document.Id; pageId = page.Id;
        db.Documents.Add(document);
        await db.SaveChangesAsync();
        store.Add("previews/source.jpg", [1, 2, 3]);
    }

    public async Task DisposeAsync() { await factory.DisposeAsync(); await postgres.DisposeAsync(); }
    private HttpClient Client(string? user = "owner", bool appCheck = true)
    {
        var client = factory.CreateClient();
        if (user is not null) client.DefaultRequestHeaders.Authorization = new("Bearer", user);
        if (appCheck) client.DefaultRequestHeaders.Add("X-Firebase-AppCheck", "valid");
        return client;
    }

    [Fact]
    public async Task Brush_only_preview_is_queued_and_oversized_masks_are_rejected()
    {
        using var owner = Client();
        var brush = new { sourceRevisionId = (Guid?)null, sourceCropRevision = 0,
            rectangles = Array.Empty<double[]>(),
            strokes = new[] { new { radius = .008,
                points = new[] { new[] { .1, .2 }, new[] { .12, .21 } } } } };
        Assert.Equal(HttpStatusCode.Accepted,
            (await owner.PostAsJsonAsync($"{Url}/previews", brush)).StatusCode);
        var tooLarge = new { sourceRevisionId = (Guid?)null, sourceCropRevision = 0,
            rectangles = Array.Empty<double[]>(),
            strokes = new[] { new { radius = .1,
                points = new[] { new[] { .1, .2 } } } } };
        Assert.Equal(HttpStatusCode.BadRequest,
            (await owner.PostAsJsonAsync($"{Url}/previews", tooLarge)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var operation = await db.PageRepairOperations.SingleAsync();
        Assert.Contains("\"radius\":0.008", operation.StrokesJson);
        Assert.Equal("[]", operation.RectanglesJson);
    }

    [Fact]
    public async Task Private_preview_requires_owner_and_app_check_and_applies_as_a_new_revision()
    {
        using var owner = Client(); using var other = Client("other");
        using var noCheck = Client(appCheck: false);
        var body = new { sourceRevisionId = (Guid?)null, sourceCropRevision = 0,
            rectangles = new[] { new[] { .01, .2, .04, .23 } } };
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await noCheck.PostAsJsonAsync($"{Url}/previews", body)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.PostAsJsonAsync($"{Url}/previews", body)).StatusCode);

        var created = await owner.PostAsJsonAsync($"{Url}/previews", body);
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("operationId").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.GetAsync($"{Url}/previews/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.GetAsync($"{Url}/previews/{id}/image")).StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var operation = await db.PageRepairOperations.SingleAsync(x => x.Id == id);
            operation.CompletePreview($"repairs/{pageId:N}/{id:N}.png");
            await db.SaveChangesAsync();
            store.Add(operation.PreviewObjectKey!, [137, 80, 78, 71, 1, 2, 3]);
        }
        var image = await owner.GetAsync($"{Url}/previews/{id}/image");
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Contains("no-store", image.Headers.CacheControl?.ToString());
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.PostAsJsonAsync($"{Url}/previews/{id}/apply", new { })).StatusCode);

        var applied = await owner.PostAsJsonAsync($"{Url}/previews/{id}/apply", new { });
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        await using var verification = factory.Services.CreateAsyncScope();
        var verifyDb = verification.ServiceProvider.GetRequiredService<AppDbContext>();
        var page = await verifyDb.Pages.Include(x => x.ActiveRevision).SingleAsync(x => x.Id == pageId);
        Assert.Equal($"repairs/{pageId:N}/{id:N}.png", page.GetProcessedObjectKey());
        Assert.Equal("previews/source.jpg", page.PreviewObjectKey);
        Assert.Equal("image/png", page.ActiveRevision?.MediaType);
        Assert.Equal(2, await verifyDb.PageRevisions.CountAsync());
        Assert.Equal("Applied", (await verifyDb.PageRepairOperations.SingleAsync()).State);
        Assert.Single(await verifyDb.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task Stale_page_rejects_old_cleanup_without_changing_active_revision()
    {
        using var owner = Client();
        var created = await owner.PostAsJsonAsync($"{Url}/previews", new {
            sourceRevisionId = (Guid?)null, sourceCropRevision = 0,
            rectangles = new[] { new[] { .01, .2, .04, .23 } }
        });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("operationId").GetGuid();
        Guid newerRevisionId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var operation = await db.PageRepairOperations.SingleAsync(x => x.Id == id);
            operation.CompletePreview($"repairs/{pageId:N}/{id:N}.png");
            var page = await db.Pages.SingleAsync(x => x.Id == pageId);
            var revision = PageRevision.CreateBase(Guid.NewGuid(), pageId,
                "previews/newer.jpg", new string('a', 64), DateTimeOffset.UtcNow);
            newerRevisionId = revision.Id;
            db.PageRevisions.Add(revision); page.ActivateRevision(revision);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict,
            (await owner.PostAsJsonAsync($"{Url}/previews/{id}/apply", new { })).StatusCode);
        await using var verification = factory.Services.CreateAsyncScope();
        var verifyDb = verification.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(newerRevisionId, (await verifyDb.Pages.SingleAsync()).ActiveRevisionId);
        Assert.Equal("Ready", (await verifyDb.PageRepairOperations.SingleAsync()).State);
    }

    [Fact]
    public async Task Crop_changed_after_page_load_rejects_the_old_cleanup_selection()
    {
        using var owner = Client();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Pages.Where(x => x.Id == pageId).ExecuteUpdateAsync(set => set
                .SetProperty(x => x.AppliedCropRevision, 1)
                .SetProperty(x => x.PreviewObjectKey, "previews/new-crop.jpg"));
        }
        var response = await owner.PostAsJsonAsync($"{Url}/previews", new {
            sourceRevisionId = (Guid?)null, sourceCropRevision = 0,
            rectangles = new[] { new[] { .01, .2, .04, .23 } }
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var verification = factory.Services.CreateAsyncScope();
        Assert.Empty(await verification.ServiceProvider.GetRequiredService<AppDbContext>()
            .PageRepairOperations.ToListAsync());
    }

    [Fact]
    public async Task Create_process_review_and_apply_repair_uses_the_same_private_preview()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "src", "ArksScanner.Worker",
            "processing", "repair_image.py"))) root = root.Parent;
        Assert.NotNull(root);
        var python = Path.Combine(root.FullName, ".task-tools", "crop-runtime", "Scripts", "python.exe");
        if (!File.Exists(python)) return; // OpenCV runtime is optional in CI.
        var fixture = Path.Combine(root.FullName, "apps", "web", "e2e", "fixtures", "append-photo.jpg");
        store.Add("previews/source.jpg", await File.ReadAllBytesAsync(fixture));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var setupDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTimeOffset.UtcNow;
            var ocr = PageOcrResult.Queue(Guid.NewGuid(), pageId, "previews/source.jpg",
                OcrSourceFingerprint.Create("previews/source.jpg"), "en", now);
            ocr.BeginAttempt(1, now);
            ocr.Complete("fixture", "v1", "SAMPLE", [OcrElement.Create(Guid.NewGuid(), ocr.Id,
                null, OcrElementKind.Word, "SAMPLE", .99, OcrTextType.Printed, 0,
                [new(.5, .5), new(.6, .5), new(.6, .55), new(.5, .55)])], now);
            setupDb.PageOcrResults.Add(ocr);
            await setupDb.SaveChangesAsync();
        }
        using var owner = Client();
        Assert.Equal("Ready", (await owner.GetFromJsonAsync<JsonElement>(
            $"/api/documents/{documentId}/pages/{pageId}/ocr")).GetProperty("state").GetString());
        var created = await owner.PostAsJsonAsync($"{Url}/previews", new {
            sourceRevisionId = (Guid?)null, sourceCropRevision = 0,
            rectangles = new[] { new[] { .145, .365, .18, .4 } }
        });
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("operationId").GetGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                ["Crop:PythonPath"] = python,
                ["Repair:ScriptPath"] = Path.Combine(root.FullName, "src", "ArksScanner.Worker",
                    "processing", "repair_image.py")
            }).Build();
            await new PageRepairProcessor(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                store, config).RunAsync(id, default);
        }
        var status = await owner.GetFromJsonAsync<JsonElement>($"{Url}/previews/{id}");
        Assert.Equal("Ready", status.GetProperty("state").GetString());
        var image = await owner.GetAsync($"{Url}/previews/{id}/image");
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        var reviewedBytes = await image.Content.ReadAsByteArrayAsync();
        Assert.True(reviewedBytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71 }));
        Assert.Equal(HttpStatusCode.OK,
            (await owner.PostAsJsonAsync($"{Url}/previews/{id}/apply", new { })).StatusCode);
        await using var verification = factory.Services.CreateAsyncScope();
        var db = verification.ServiceProvider.GetRequiredService<AppDbContext>();
        var page = await db.Pages.Include(x => x.ActiveRevision).SingleAsync(x => x.Id == pageId);
        Assert.Equal(reviewedBytes, store.Read(page.GetProcessedObjectKey()));
        Assert.Equal("image/png", page.ActiveRevision!.MediaType);
        Assert.Equal("NotRequested", (await owner.GetFromJsonAsync<JsonElement>(
            $"/api/documents/{documentId}/pages/{pageId}/ocr")).GetProperty("state").GetString());
        var exportResponse = await owner.PostAsJsonAsync($"/api/documents/{documentId}/exports",
            new { pageLayout = "A4" });
        Assert.Equal(HttpStatusCode.Accepted, exportResponse.StatusCode);
        await using var exportScope = factory.Services.CreateAsyncScope();
        var exportDb = exportScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var export = await exportDb.DocumentExports.SingleAsync();
        var snapshot = JsonDocument.Parse(export.SnapshotJson).RootElement[0];
        Assert.Equal("A4", snapshot.GetProperty("PageLayout").GetString());
        Assert.Equal(page.GetProcessedObjectKey(), snapshot.GetProperty("ProcessedObjectKey").GetString());
        Assert.Equal(JsonValueKind.Null, snapshot.GetProperty("OcrResultId").ValueKind);
        await new DocumentPdfBuilder(exportDb, store,
            exportScope.ServiceProvider.GetRequiredService<IClock>()).BuildAsync(export.Id, default);
        var built = await exportDb.DocumentExports.AsNoTracking().SingleAsync();
        Assert.Equal(DocumentExportState.Ready, built.State);
        var pdfBytes = store.Read(built.OutputObjectKey!);
        using var pdf = PdfReader.Open(new MemoryStream(pdfBytes), PdfDocumentOpenMode.Import);
        Assert.Single(pdf.Pages);
        Assert.InRange(pdf.Pages[0].Width.Point, 595.1, 595.4);
        Assert.InRange(pdf.Pages[0].Height.Point, 841.7, 842.0);
    }

    private sealed class Identity : IRequestIdentityVerifier
    {
        public Task<VerifiedRequestIdentity> VerifyAsync(string idToken, string appCheckToken, CancellationToken ct) =>
            appCheckToken == "valid" ? Task.FromResult(new VerifiedRequestIdentity(idToken, "test@example.test")) :
                throw new UnauthorizedAccessException();
    }

    private sealed class MemoryStore : IObjectStore
    {
        private readonly Dictionary<string, byte[]> data = new(StringComparer.Ordinal);
        public void Add(string key, byte[] bytes) => data[key] = bytes;
        public byte[] Read(string key) => data[key];
        public Task<Uri> CreatePutUrlAsync(PutObjectRequest request, CancellationToken ct) =>
            Task.FromResult(new Uri("https://example.test/upload"));
        public Task<StoredObjectInfo?> HeadAsync(string key, CancellationToken ct) =>
            Task.FromResult<StoredObjectInfo?>(data.TryGetValue(key, out var bytes) ?
                new StoredObjectInfo(bytes.Length, "image/png", "test") : null);
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) =>
            Task.FromResult<Stream>(new MemoryStream(data[key], writable: false));
        public Task PromoteAsync(string source, string destination, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct) => Task.CompletedTask;
        public async Task<ObjectCreationResult> WriteIfAbsentAsync(string key, string mediaType,
            Stream content, CancellationToken ct)
        {
            if (data.ContainsKey(key)) return ObjectCreationResult.AlreadyExists;
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, ct);
            data.Add(key, copy.ToArray());
            return ObjectCreationResult.Created;
        }
    }
}
