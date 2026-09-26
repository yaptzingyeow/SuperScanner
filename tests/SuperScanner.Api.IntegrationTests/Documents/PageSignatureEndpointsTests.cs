using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ImageMagick;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Documents;
using SuperScanner.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SuperScanner.Api.IntegrationTests.Documents;

public sealed class PageSignatureEndpointsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly MemoryStore store = new();
    private WebApplicationFactory<Program> factory = null!;
    private Guid documentId, pageId;
    private string Url => $"/api/documents/{documentId}/pages/{pageId}/signatures";

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        var connection = postgres.GetConnectionString();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Audit:SigningKeyBase64", Convert.ToBase64String(new byte[32]));
            builder.UseSetting("Audit:SigningKeyId", "test");
            builder.ConfigureLogging(logging => logging.ClearProviders());
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
        var document = Document.Create(Guid.NewGuid(), "owner", "Signature test", DateTimeOffset.UtcNow);
        var page = document.AddPage(Guid.NewGuid(), 20, DateTimeOffset.UtcNow);
        documentId = document.Id; pageId = page.Id;
        db.Documents.Add(document);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() { await factory.DisposeAsync(); await postgres.DisposeAsync(); }
    private HttpClient Client(string? user = "owner", bool appCheck = true)
    {
        var client = factory.CreateClient();
        if (user != null) client.DefaultRequestHeaders.Authorization = new("Bearer", user);
        if (appCheck) client.DefaultRequestHeaders.Add("X-Firebase-AppCheck", "valid");
        return client;
    }
    private static byte[] Png()
    {
        using var image = new MagickImage(MagickColors.Black, 20, 10);
        return image.ToByteArray(MagickFormat.Png);
    }
    private async Task<HttpResponseMessage> Create(HttpClient client, Guid? requestId = null, byte[]? bytes = null)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes ?? Png()), "image", "signature.png");
        form.Add(new StringContent("{\"x\":0.1,\"y\":0.2,\"width\":0.3,\"height\":0.15}"), "box");
        using var request = new HttpRequestMessage(HttpMethod.Post, Url) { Content = form };
        request.Headers.Add("Idempotency-Key", (requestId ?? Guid.NewGuid()).ToString());
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Create_read_retry_update_delete_are_owner_scoped_and_revision_checked()
    {
        using var client = Client();
        var requestId = Guid.NewGuid();
        var response = await Create(client, requestId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = dto.GetProperty("id").GetGuid();
        Assert.False(dto.TryGetProperty("assetKey", out _));
        var retry = await Create(client, requestId);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(id, (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        Assert.Single(store.Objects);
        var imageUrl = dto.GetProperty("imageUrl").GetString()!;
        var image = await client.GetAsync(imageUrl);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType!.MediaType);
        Assert.True(image.Headers.CacheControl!.NoStore);
        using var other = Client("other");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(imageUrl)).StatusCode);
        var update = new { box = new { x = .2, y = .2, width = .3, height = .15 }, expectedRevision = 0 };
        Assert.Equal(HttpStatusCode.NotFound, (await Create(other)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"{Url}/{id}", update)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"{Url}/{id}?expectedRevision=0")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"{Url}/{id}", update)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"{Url}/{id}", update)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{Url}/{id}?expectedRevision=1")).StatusCode);
        Assert.Empty(await client.GetFromJsonAsync<JsonElement[]>(Url) ?? []);
        Assert.Single(store.Objects); // Immutable export snapshots may still reference it.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(4, (await db.Documents.SingleAsync()).Revision); // Add page + 3 signature mutations.
        Assert.Equal(3, await db.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task Authentication_app_check_and_valid_png_are_required()
    {
        using var noAuth = Client(null); using var noCheck = Client(appCheck: false); using var owner = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Create(noAuth)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Create(noCheck)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Create(owner, bytes: [1, 2, 3])).StatusCode);
        Assert.Empty(store.Objects);
    }

    [Fact]
    public async Task Failed_database_commit_removes_new_asset()
    {
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE page_signatures ADD CONSTRAINT reject_signature CHECK (false)");
        }
        using var owner = Client();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Create(owner)).StatusCode);
        Assert.Empty(store.Objects);
        await using var verify = factory.Services.CreateAsyncScope();
        Assert.Empty(await verify.ServiceProvider.GetRequiredService<AppDbContext>().PageSignatures.ToListAsync());
    }

    [Fact]
    public async Task Failed_create_with_storage_outage_leaves_durable_cleanup_intent()
    {
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE page_signatures ADD CONSTRAINT reject_signature CHECK (false)");
        }
        store.FailDelete = true;
        using var owner = Client();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Create(owner)).StatusCode);
        await using var verify = factory.Services.CreateAsyncScope();
        var saved = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(await saved.SignatureAssetWriteIntents.ToListAsync());
        Assert.Single(store.Objects);
    }

    [Fact]
    public async Task Removed_page_is_hidden_and_cannot_receive_a_signature()
    {
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var document = await db.Documents.Include(x => x.Pages).SingleAsync();
            document.RemovePage(pageId, "owner", DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }
        using var owner = Client();
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Create(owner)).StatusCode);
        Assert.Empty(store.Objects);
    }

    [Fact]
    public async Task Idempotency_key_cannot_be_reused_for_different_ink()
    {
        using var owner = Client();
        var key = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await Create(owner, key)).StatusCode);
        using var image = new MagickImage(MagickColors.Blue, 20, 10);
        Assert.Equal(HttpStatusCode.Conflict, (await Create(owner, key, image.ToByteArray(MagickFormat.Png))).StatusCode);
        Assert.Single(store.Objects);
    }

    [Fact]
    public async Task Missing_box_on_update_is_rejected_without_changing_placement()
    {
        using var owner = Client();
        var response = await Create(owner);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await owner.PutAsJsonAsync($"{Url}/{id}", new { expectedRevision = 0 })).StatusCode);
    }

    private sealed class Identity : IRequestIdentityVerifier
    {
        public Task<VerifiedRequestIdentity> VerifyAsync(string idToken, string appCheckToken, CancellationToken ct) =>
            appCheckToken == "valid" ? Task.FromResult(new VerifiedRequestIdentity(idToken, "test@example.test")) :
                Task.FromException<VerifiedRequestIdentity>(new UnauthorizedAccessException());
    }
    private sealed class MemoryStore : IObjectStore
    {
        public bool FailDelete { get; set; }
        public Dictionary<string, byte[]> Objects { get; } = [];
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream(Objects[key]));
        public async Task<ObjectCreationResult> WriteIfAbsentAsync(string key, string mediaType, Stream content, CancellationToken ct)
        {
            using var output = new MemoryStream(); await content.CopyToAsync(output, ct);
            return Objects.TryAdd(key, output.ToArray()) ? ObjectCreationResult.Created : ObjectCreationResult.AlreadyExists;
        }
        public Task DeleteAsync(string key, CancellationToken ct) { if (FailDelete) throw new IOException("storage outage"); Objects.Remove(key); return Task.CompletedTask; }
        public Task<Uri> CreatePutUrlAsync(PutObjectRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<StoredObjectInfo?> HeadAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public Task PromoteAsync(string from, string to, CancellationToken ct) => throw new NotSupportedException();
    }
}
