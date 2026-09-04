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

namespace SuperScanner.Api.IntegrationTests.Uploads;

public sealed class UploadOwnershipTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private WebApplicationFactory<Program>? _factory;

    [Fact]
    public async Task CreateIntent_ReturnsNotFoundForAnotherUsersDocument()
    {
        using var userA = CreateAuthenticatedClient("user-a");
        using var userB = CreateAuthenticatedClient("user-b");
        var createDocument = await userA.PostAsJsonAsync("/api/documents", new { title = "Private form" });
        createDocument.EnsureSuccessStatusCode();
        var document = await createDocument.Content.ReadFromJsonAsync<DocumentSummary>();

        var ownerResponse = await userA.PostAsJsonAsync(
            $"/api/documents/{document!.Id}/uploads",
            ValidUploadRequest());
        var ownerError = await ownerResponse.Content.ReadAsStringAsync();
        Assert.True(ownerResponse.IsSuccessStatusCode, ownerError);

        var response = await userB.PostAsJsonAsync(
            $"/api/documents/{document.Id}/uploads",
            ValidUploadRequest());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Complete_ReturnsNotFoundForAnotherUsersUpload()
    {
        using var userA = CreateAuthenticatedClient("user-a");
        using var userB = CreateAuthenticatedClient("user-b");
        var createDocument = await userA.PostAsJsonAsync("/api/documents", new { title = "Private form" });
        createDocument.EnsureSuccessStatusCode();
        var document = await createDocument.Content.ReadFromJsonAsync<DocumentSummary>();
        var createUpload = await userA.PostAsJsonAsync(
            $"/api/documents/{document!.Id}/uploads",
            ValidUploadRequest());
        createUpload.EnsureSuccessStatusCode();
        var upload = await createUpload.Content.ReadFromJsonAsync<UploadIntentDto>();

        var ownerResponse = await userA.PostAsync(
            $"/api/documents/{document.Id}/uploads/{upload!.UploadId}/complete",
            content: null);
        var ownerError = await ownerResponse.Content.ReadAsStringAsync();
        Assert.True(ownerResponse.IsSuccessStatusCode, ownerError);

        var response = await userB.PostAsync(
            $"/api/documents/{document.Id}/uploads/{upload.UploadId}/complete",
            content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options;
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
                    services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
                    services.RemoveAll<IObjectStore>();
                    services.AddSingleton<IObjectStore, FakeObjectStore>();
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

    private static object ValidUploadRequest() => new
    {
        fileName = "private.pdf",
        mediaType = "application/pdf",
        sizeBytes = 1200,
        sha256Hex = new string('a', 64)
    };

    private sealed record DocumentSummary(Guid Id);
    private sealed record UploadIntentDto(Guid UploadId);

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

    private sealed class FakeObjectStore : IObjectStore
    {
        public Task<Uri> CreatePutUrlAsync(PutObjectRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new Uri("https://uploads.example.test/opaque"));

        public Task<StoredObjectInfo?> HeadAsync(string objectKey, CancellationToken cancellationToken) =>
            Task.FromResult<StoredObjectInfo?>(new StoredObjectInfo(1200, "application/pdf", "etag"));

        public Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task PromoteAsync(
            string quarantineKey,
            string acceptedKey,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
