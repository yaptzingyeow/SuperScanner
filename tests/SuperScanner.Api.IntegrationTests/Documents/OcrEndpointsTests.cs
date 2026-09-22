using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.Ocr;

namespace SuperScanner.Api.IntegrationTests.Documents;

public sealed class OcrEndpointsTests
{
    private static readonly Guid DocumentId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid PageId = Guid.Parse("20000000-0000-0000-0000-000000000002");

    [Fact]
    public async Task PostThenGet_ReturnsAcceptedAndCurrentStatus()
    {
        var repository = new OcrRepositoryStub(PageState.Ready, "previews/document/page.jpg");
        await using var factory = CreateFactory(repository, enabled: true);
        using var client = Client(factory);

        var post = await client.PostAsJsonAsync(Url(), new { retryFailed = false });

        Assert.Equal(HttpStatusCode.Accepted, post.StatusCode);
        var accepted = await post.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Queued", accepted.GetProperty("state").GetString());
        Assert.False(accepted.TryGetProperty("fullText", out var text) && text.ValueKind != JsonValueKind.Null);
        var get = await client.GetAsync(Url());
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("Queued", (await get.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state").GetString());
        Assert.Equal(1, repository.Queue.EnqueueCount);
    }

    [Fact]
    public async Task GetBeforeRequest_ReturnsNotRequested()
    {
        await using var factory = CreateFactory(new OcrRepositoryStub(PageState.Ready, "previews/page.jpg"), enabled: true);
        using var client = Client(factory);

        var response = await client.GetAsync(Url());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("NotRequested", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state").GetString());
    }

    [Theory]
    [InlineData("user-b")]
    [InlineData("user-a")]
    public async Task MissingOrUnownedPage_IsHiddenAsNotFound(string user)
    {
        var repository = new OcrRepositoryStub(PageState.Ready, "previews/page.jpg") { ResourceExists = false };
        await using var factory = CreateFactory(repository, enabled: true);
        using var client = Client(factory, user);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync(Url(), new { retryFailed = false })).StatusCode);
    }

    [Fact]
    public async Task PostForNonReadyPage_ReturnsStableConflict()
    {
        await using var factory = CreateFactory(new OcrRepositoryStub(PageState.Processing, "previews/page.jpg"), enabled: true);
        using var client = Client(factory);

        var response = await client.PostAsJsonAsync(Url(), new { retryFailed = false });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("page_not_ready", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task DisabledPost_ReturnsServiceUnavailableWithoutMutation()
    {
        var repository = new OcrRepositoryStub(PageState.Ready, "previews/page.jpg");
        await using var factory = CreateFactory(repository, enabled: false);
        using var client = Client(factory);

        var response = await client.PostAsJsonAsync(Url(), new { retryFailed = false });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("ocr_disabled", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Null(repository.Result);
        Assert.Equal(0, repository.Queue.EnqueueCount);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("user-a", false)]
    [InlineData("invalid", true)]
    public async Task Routes_RequireVerifiedIdentityAndAppCheck(string? user, bool appCheck)
    {
        await using var factory = CreateFactory(new OcrRepositoryStub(PageState.Ready, "previews/page.jpg"), enabled: true);
        using var client = Client(factory, user, appCheck);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Url())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync(Url(), new { retryFailed = false })).StatusCode);
    }

    private static string Url() => $"/api/documents/{DocumentId}/pages/{PageId}/ocr";

    private static WebApplicationFactory<Program> CreateFactory(OcrRepositoryStub repository, bool enabled) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.UseSetting("Audit:SigningKeyBase64", Convert.ToBase64String(new byte[32]));
            builder.UseSetting("Audit:SigningKeyId", "ocr-tests");
            builder.UseSetting("Ocr:Enabled", enabled.ToString());
            builder.UseSetting("Ocr:Provider", enabled ? "Fake" : "Disabled");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRequestIdentityVerifier>();
                services.AddSingleton<IRequestIdentityVerifier, IdentityVerifier>();
                services.RemoveAll<IOcrRepository>();
                services.AddSingleton<IOcrRepository>(repository);
                services.RemoveAll<IProcessingJobQueue>();
                services.AddSingleton<IProcessingJobQueue>(repository.Queue);
            });
        });

    private static HttpClient Client(WebApplicationFactory<Program> factory, string? user = "user-a", bool appCheck = true)
    {
        var client = factory.CreateClient();
        if (user is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user);
        if (appCheck) client.DefaultRequestHeaders.Add("X-Firebase-AppCheck", "valid-app");
        return client;
    }

    private sealed class IdentityVerifier : IRequestIdentityVerifier
    {
        public Task<VerifiedRequestIdentity> VerifyAsync(string idToken, string appCheckToken, CancellationToken ct) =>
            idToken is "user-a" or "user-b" && appCheckToken == "valid-app"
                ? Task.FromResult(new VerifiedRequestIdentity(idToken, "test@example.test"))
                : Task.FromException<VerifiedRequestIdentity>(new UnauthorizedAccessException());
    }

    private sealed class OcrRepositoryStub(PageState state, string? sourceKey) : IOcrRepository
    {
        public bool ResourceExists { get; set; } = true;
        public PageOcrResult? Result { get; private set; }
        public QueueStub Queue { get; } = new();

        public Task<IOcrTransaction> BeginTransactionAsync(CancellationToken ct) =>
            Task.FromResult<IOcrTransaction>(new TransactionStub());

        public Task<OcrPageSource?> FindOwnedSourceAsync(string ownerUid, Guid documentId, Guid pageId,
            bool forUpdate, CancellationToken ct) => Task.FromResult<OcrPageSource?>(
                ResourceExists && ownerUid == "user-a" && documentId == DocumentId && pageId == PageId
                    ? new(PageId, state, sourceKey, "image/jpeg")
                    : null);

        public Task<PageOcrResult?> FindBySourceAsync(Guid pageId, string sourceFingerprint, bool forUpdate,
            CancellationToken ct) => Task.FromResult(Result);

        public Task AddAsync(PageOcrResult result, CancellationToken ct)
        {
            Result = result;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;

        private sealed class TransactionStub : IOcrTransaction
        {
            public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class QueueStub : IProcessingJobQueue
    {
        public int EnqueueCount { get; private set; }
        public Task EnqueueAsync(string type, string payload, string idempotencyKey, CancellationToken cancellationToken)
        {
            EnqueueCount++;
            return Task.CompletedTask;
        }
        public Task<ProcessingJobLease?> TryLeaseAsync(string workerId, TimeSpan leaseDuration, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HeartbeatAsync(Guid jobId, string workerId, TimeSpan leaseDuration, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CompleteAsync(Guid jobId, string workerId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RescheduleAsync(Guid jobId, string workerId, string errorCode, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
