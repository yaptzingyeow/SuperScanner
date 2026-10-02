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

public sealed class CropEndpointsRotationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private WebApplicationFactory<Program> factory = null!;
    private Guid documentId, pageId;
    private string Url => $"/api/documents/{documentId}/pages/{pageId}/crop";

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
        var document = Document.Create(Guid.NewGuid(), "owner", "Rotation test", DateTimeOffset.UtcNow);
        var page = document.AddPage(Guid.NewGuid(), 20, DateTimeOffset.UtcNow);
        page.MarkImportReady("originals/source.jpg", "image/jpeg");
        page.SetPreview("previews/source.jpg", "thumbnails/source.jpg");
        page.InitializeCrop();
        documentId = document.Id; pageId = page.Id;
        db.Documents.Add(document);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() { await factory.DisposeAsync(); await postgres.DisposeAsync(); }

    private HttpClient Client()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "owner");
        client.DefaultRequestHeaders.Add("X-Firebase-AppCheck", "valid");
        return client;
    }

    private static readonly object[] FullImage =
        [new { x = 0.0, y = 0.0 }, new { x = 1.0, y = 0.0 }, new { x = 1.0, y = 1.0 }, new { x = 0.0, y = 1.0 }];

    [Fact]
    public async Task Apply_with_rotation_persists_and_returns_it()
    {
        using var client = Client();
        var response = await client.PostAsJsonAsync($"{Url}/apply",
            new { revision = 1, points = FullImage, filter = "Original", rotation = 270 });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var read = await client.GetAsync(Url);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var body = await read.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(270, body.GetProperty("rotation").GetInt32());
    }

    [Fact]
    public async Task Apply_with_invalid_rotation_is_a_validation_error()
    {
        using var client = Client();
        var response = await client.PostAsJsonAsync($"{Url}/apply",
            new { revision = 1, points = FullImage, rotation = 45 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("errors").TryGetProperty("rotation", out _));
    }

    private sealed class Identity : IRequestIdentityVerifier
    {
        public Task<VerifiedRequestIdentity> VerifyAsync(string idToken, string appCheckToken, CancellationToken ct) =>
            appCheckToken == "valid" ? Task.FromResult(new VerifiedRequestIdentity(idToken, "test@example.test")) :
                throw new UnauthorizedAccessException();
    }
}
