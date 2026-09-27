using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SuperScanner.Api.Auth;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Documents;
using SuperScanner.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SuperScanner.Api.IntegrationTests.Documents;

public sealed class PageMarkEndpointsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private WebApplicationFactory<Program> factory = null!;
    private Guid documentId, pageId;
    private string Url => $"/api/documents/{documentId}/pages/{pageId}/marks";

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
            });
        });
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        var document = Document.Create(Guid.NewGuid(), "owner", "Mark test", DateTimeOffset.UtcNow);
        var page = document.AddPage(Guid.NewGuid(), 20, DateTimeOffset.UtcNow);
        documentId = document.Id; pageId = page.Id;
        db.Documents.Add(document);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() { await factory.DisposeAsync(); await postgres.DisposeAsync(); }
    private HttpClient Client(string? user = "owner", bool appCheck = true)
    {
        var client = factory.CreateClient();
        if (user is not null) client.DefaultRequestHeaders.Authorization = new("Bearer", user);
        if (appCheck) client.DefaultRequestHeaders.Add("X-Firebase-AppCheck", "valid");
        return client;
    }
    private static object Input(string kind = "Check", string color = "#000000", double strokeWidth = .08) =>
        new { kind, box = new { x = .1, y = .2, width = .03, height = .04 }, color, strokeWidth };
    private async Task<HttpResponseMessage> Create(HttpClient client, Guid key, object? input = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url) { Content = JsonContent.Create(input ?? Input()) };
        request.Headers.Add("Idempotency-Key", key.ToString());
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Owner_can_create_retry_update_and_delete_with_revision_checks()
    {
        using var owner = Client();
        var key = Guid.NewGuid();
        var created = await Create(owner, key);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var dto = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = dto.GetProperty("id").GetGuid();
        Assert.Equal(id, (await (await Create(owner, key)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await Create(owner, key, Input("Cross"))).StatusCode);
        var update = new { kind = "Cross", box = new { x = .2, y = .3, width = .04, height = .05 }, color = "#f00000", strokeWidth = .12, expectedRevision = 0 };
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"{Url}/{id}", update)).StatusCode);
        Assert.Equal(id, (await (await Create(owner, key)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PutAsJsonAsync($"{Url}/{id}", update)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"{Url}/{id}?expectedRevision=1")).StatusCode);
        var deletedRetry = await (await Create(owner, key)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, deletedRetry.GetProperty("id").GetGuid());
        Assert.True(deletedRetry.GetProperty("isDeleted").GetBoolean());
        Assert.Empty(await owner.GetFromJsonAsync<JsonElement[]>(Url) ?? []);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(4, (await db.Documents.SingleAsync()).Revision);
        Assert.Equal(3, await db.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task Other_owner_and_removed_page_cannot_access_marks()
    {
        using var other = Client("other");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Create(other, Guid.NewGuid())).StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var document = await db.Documents.Include(x => x.Pages).SingleAsync();
            document.RemovePage(pageId, "owner", DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }
        using var owner = Client();
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Create(owner, Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task Invalid_values_and_missing_auth_are_rejected()
    {
        using var owner = Client(); using var noAuth = Client(null); using var noCheck = Client(appCheck: false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Create(noAuth, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Create(noCheck, Guid.NewGuid())).StatusCode);
        foreach (var input in new[] { Input("Unknown"), Input(color: "blue"), Input(strokeWidth: .5) })
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Create(owner, Guid.NewGuid(), input)).StatusCode);
    }

    [Fact]
    public async Task Legacy_mark_without_original_hash_still_reuses_its_create_identity()
    {
        using var owner = Client();
        var key = Guid.NewGuid();
        var created = await Create(owner, key);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var update = new { kind = "Cross", box = new { x = .2, y = .3, width = .04, height = .05 },
            color = "#f00000", strokeWidth = .12, expectedRevision = 0 };
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"{Url}/{id}", update)).StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE page_marks SET \"CreateRequestHash\" = NULL WHERE \"Id\" = {id}");
        }
        var retry = await Create(owner, key);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(id, (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
    }

    private sealed class Identity : IRequestIdentityVerifier
    {
        public Task<VerifiedRequestIdentity> VerifyAsync(string idToken, string appCheckToken, CancellationToken ct) =>
            appCheckToken == "valid" ? Task.FromResult(new VerifiedRequestIdentity(idToken, "test@example.test")) :
                throw new UnauthorizedAccessException();
    }
}
