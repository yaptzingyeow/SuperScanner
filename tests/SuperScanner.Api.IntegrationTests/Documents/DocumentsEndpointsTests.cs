using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SuperScanner.Application.Abstractions;
using SuperScanner.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using System.Text.Json;
using SuperScanner.Api.Endpoints;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.Uploads;

namespace SuperScanner.Api.IntegrationTests.Documents;

public sealed class DocumentsEndpointsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private WebApplicationFactory<Program>? _factory;

    public static TheoryData<string?> InvalidTitles => new()
    {
        null,
        "",
        "   ",
        new string('x', 201)
    };

    [Fact]
    public async Task Detail_HidesUnownedDocumentAndReturnsOrganizerContract()
    {
        var fixture = OrganizerFixture.Create();
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AddRange(fixture.Document, fixture.Upload, fixture.Export);
            await db.SaveChangesAsync();
        }
        using var owner = CreateAuthenticatedClient("user-a");
        using var other = CreateAuthenticatedClient("user-b");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/documents/{fixture.Document.Id}")).StatusCode);
        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/documents/{fixture.Document.Id}");
        OrganizerFixture.AssertDetail(fixture, detail);
    }

    [Fact]
    public async Task List_ReturnsOnlyCurrentUsersDocuments()
    {
        using var userA = CreateAuthenticatedClient("user-a");
        using var userB = CreateAuthenticatedClient("user-b");
        var userACreate = await userA.PostAsJsonAsync("/api/documents", new { title = "A form" });
        var userBCreate = await userB.PostAsJsonAsync("/api/documents", new { title = "B form" });
        userACreate.EnsureSuccessStatusCode();
        userBCreate.EnsureSuccessStatusCode();

        var documents = await userA.GetFromJsonAsync<List<DocumentSummary>>("/api/documents");

        var document = Assert.Single(documents!);
        Assert.Equal("A form", document.Title);
    }

    [Theory]
    [MemberData(nameof(InvalidTitles))]
    public async Task Create_RejectsTitleOutsideOneToTwoHundredTrimmedCharacters(string? title)
    {
        using var client = CreateAuthenticatedClient("user-a");

        var response = await client.PostAsJsonAsync("/api/documents", new { title });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Create_TrimsTitleBeforeApplyingTheLengthLimit()
    {
        using var client = CreateAuthenticatedClient("user-a");
        var expectedTitle = new string('x', 200);

        var response = await client.PostAsJsonAsync(
            "/api/documents",
            new { title = $"  {expectedTitle}  " });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<DocumentSummary>();
        Assert.Equal(expectedTitle, document!.Title);
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting(
                    "Audit:SigningKeyBase64",
                    Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray()));
                builder.UseSetting("Audit:SigningKeyId", "api-test-key");
                builder.ConfigureLogging(logging => logging.ClearProviders());
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IRequestIdentityVerifier>();
                    services.AddSingleton<IRequestIdentityVerifier, FakeRequestIdentityVerifier>();
                    services.RemoveAll<DbContextOptions<AppDbContext>>();
                    services.RemoveAll<AppDbContext>();
                    services.AddDbContext<AppDbContext>(dbOptions => dbOptions.UseNpgsql(connectionString));
                });
            });
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }

    private HttpClient CreateAuthenticatedClient(string firebaseUid)
    {
        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", firebaseUid);
        client.DefaultRequestHeaders.Add("X-Firebase-AppCheck", "valid-app");
        return client;
    }

    private sealed record DocumentSummary(Guid Id, string Title, string Status, int PageCount, DateTimeOffset UpdatedAt);

    private sealed class FakeRequestIdentityVerifier : IRequestIdentityVerifier
    {
        public Task<VerifiedRequestIdentity> VerifyAsync(
            string idToken,
            string appCheckToken,
            CancellationToken cancellationToken) =>
            appCheckToken == "valid-app" && !string.IsNullOrWhiteSpace(idToken)
                ? Task.FromResult(new VerifiedRequestIdentity(idToken, $"{idToken}@example.test"))
                : Task.FromException<VerifiedRequestIdentity>(new UnauthorizedAccessException());
    }
}

// Contract projection runs without Docker; HTTP/database ownership coverage above remains PostgreSQL-backed.
public sealed class DocumentsEndpointsContractTests
{
    [Fact]
    public void Detail_ProjectsOrderedActivePagesRevisionsImportAndLatestExport()
    {
        var fixture = OrganizerFixture.Create();
        var detail = DocumentPreviewEndpoints.CreateDetail(fixture.Document, [fixture.Upload], fixture.Export);
        OrganizerFixture.AssertDetail(fixture, JsonSerializer.SerializeToElement(detail, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public void Detail_DoesNotExposeUnknownFailureTextOrPrivateKeys()
    {
        var fixture = OrganizerFixture.Create();
        fixture.Document.ActivePages.Last().MarkFailed("private storage URL and exception");
        fixture.Upload.RecordExpansion(1, 1, "private storage URL and exception");
        fixture.Export.Fail("private storage URL and exception", DateTimeOffset.UtcNow);
        var detail = DocumentPreviewEndpoints.CreateDetail(fixture.Document, [fixture.Upload], fixture.Export);
        var json = JsonSerializer.Serialize(detail, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("private storage", json);
        Assert.DoesNotContain("imports/", json);
        Assert.DoesNotContain("previews/", json);
        Assert.DoesNotContain("snapshotJson", json);
    }
}

internal sealed record OrganizerFixture(Document Document, UploadIntent Upload, DocumentExport Export, Guid ReadyId, Guid FailedId)
{
    public static OrganizerFixture Create()
    {
        var now = DateTimeOffset.UtcNow;
        var document = Document.Create(Guid.NewGuid(), "user-a", "Organizer", now);
        var upload = UploadIntent.Create(Guid.NewGuid(), "user-a", document.Id, "quarantine/source", "scan.pdf",
            "application/pdf", 100, new string('a', 64), now.AddHours(1));
        upload.TryMarkPendingValidation(now);
        upload.Accept("imports/private", now);
        upload.BeginExpansion(3);
        upload.RecordExpansion(1, 1, "pdf_render_failed");
        var pages = document.AppendImportedPages(upload.Id, [1, 2, 3], 50, now);
        pages[0].MarkFailed("pdf_render_failed");
        pages[1].MarkImportReady("imports/private", "image/png");
        pages[1].SetPreview("previews/private", "thumb");
        pages[1].MarkReady();
        var export = DocumentExport.Create(Guid.NewGuid(), document, "user-a", now, TimeSpan.FromDays(7));
        document.RemovePage(pages[2].Id, "user-a", now);
        document.ReorderPages([pages[1].Id, pages[0].Id], document.PageOrderRevision, now);
        return new(document, upload, export, pages[1].Id, pages[0].Id);
    }

    public static void AssertDetail(OrganizerFixture fixture, JsonElement detail)
    {
        Assert.Equal("Ready", detail.GetProperty("status").GetString());
        Assert.Equal(3, detail.GetProperty("revision").GetInt64());
        Assert.Equal(3, detail.GetProperty("pageOrderRevision").GetInt64());
        var pages = detail.GetProperty("pages").EnumerateArray().ToArray();
        Assert.Equal(new[] { fixture.ReadyId, fixture.FailedId }, pages.Select(p => p.GetProperty("id").GetGuid()));
        Assert.Equal(new[] { 1, 2 }, pages.Select(p => p.GetProperty("position").GetInt32()));
        Assert.Equal("Ready", pages[0].GetProperty("state").GetString());
        Assert.Equal("Failed", pages[1].GetProperty("state").GetString());
        Assert.Equal("pdf_render_failed", pages[1].GetProperty("failureCode").GetString());
        Assert.Equal(0, pages[0].GetProperty("cropRevision").GetInt32());
        Assert.Equal(0, pages[0].GetProperty("appliedCropRevision").GetInt32());
        Assert.True(pages[0].GetProperty("hasPreview").GetBoolean());
        var import = Assert.Single(detail.GetProperty("imports").EnumerateArray());
        Assert.Equal(fixture.Upload.Id, import.GetProperty("uploadId").GetGuid());
        Assert.Equal("scan.pdf", import.GetProperty("fileName").GetString());
        Assert.Equal(3, import.GetProperty("discoveredPageCount").GetInt32());
        Assert.Equal(1, import.GetProperty("createdPageCount").GetInt32());
        Assert.Equal(1, import.GetProperty("failedPageCount").GetInt32());
        Assert.Equal("pdf_render_failed", import.GetProperty("errorCode").GetString());
        var export = detail.GetProperty("latestExport");
        Assert.Equal(fixture.Export.Id, export.GetProperty("id").GetGuid());
        Assert.True(export.GetProperty("isOutdated").GetBoolean());
        Assert.Equal(1, export.GetProperty("readyPageCount").GetInt32());
        Assert.Equal(2, export.GetProperty("excludedPageCount").GetInt32());
    }
}
