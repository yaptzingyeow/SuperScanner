using Microsoft.Extensions.Options;
using SuperScanner.Application.Abstractions;
using SuperScanner.Infrastructure.ObjectStorage;
using System.Collections.Concurrent;
using System.Net;
using Amazon.Runtime;
using Amazon.S3;

namespace SuperScanner.Infrastructure.IntegrationTests.ObjectStorage;

public sealed class R2ObjectStoreTests
{
    [Fact]
    public async Task CreateOnly_DelayedOlderPutCannotOverwriteNewerObject_UsingRealSdkConditionalHeader()
    {
        using var transport = new ConditionalTransport { DelayFirst = true };
        using var store = CreateStore(transport);
        using var stale = new MemoryStream("stale worker PDF"u8.ToArray());
        using var winner = new MemoryStream("published PDF"u8.ToArray());
        var late = store.WriteIfAbsentAsync("exports/document/export/document.pdf", "application/pdf", stale, default);
        await transport.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.Equal(ObjectCreationResult.Created,
                await store.WriteIfAbsentAsync("exports/document/export/document.pdf", "application/pdf", winner, default));
        }
        finally { transport.ReleaseFirst.TrySetResult(); }
        Assert.Equal(ObjectCreationResult.AlreadyExists, await late);
        Assert.Equal("published PDF"u8.ToArray(), transport.Objects.Values.Single());
        Assert.Equal(new[] { "*", "*" }, transport.Conditions);
        Assert.All(transport.Methods, method => Assert.Equal(HttpMethod.Put, method));
    }

    [Theory]
    [InlineData(409, "ConditionalRequestConflict")]
    [InlineData(403, "AccessDenied")]
    [InlineData(412, "UnexpectedPrecondition")]
    public async Task CreateOnly_DoesNotMisclassifyOtherFailuresAsExistingObject(int status, string code)
    {
        using var transport = new ConditionalTransport { FailureStatus = (HttpStatusCode)status, FailureCode = code };
        using var store = CreateStore(transport);
        using var pdf = new MemoryStream("PDF"u8.ToArray());
        var error = await Assert.ThrowsAsync<AmazonS3Exception>(() => store.WriteIfAbsentAsync("exports/key", "application/pdf", pdf, default));
        Assert.Equal((HttpStatusCode)status, error.StatusCode);
        Assert.Empty(transport.Objects);
        Assert.Equal("*", Assert.Single(transport.Conditions));
    }

    [Fact]
    public async Task MutableWrite_StillReplacesItsObjectWithoutCreateOnlyCondition()
    {
        using var transport = new ConditionalTransport();
        using var store = CreateStore(transport);
        using var original = new MemoryStream("old"u8.ToArray());
        using var updated = new MemoryStream("new"u8.ToArray());
        await store.WriteAsync("previews/mutable", "image/jpeg", original, default);
        await store.WriteAsync("previews/mutable", "image/jpeg", updated, default);
        Assert.Equal("new"u8.ToArray(), transport.Objects.Values.Single());
        Assert.All(transport.Conditions, condition => Assert.Null(condition));
    }

    [Fact]
    public async Task CreateOnly_CancellationAfterRemoteCommit_PropagatesAndRetryCannotReplaceBytes()
    {
        using var cancellation = new CancellationTokenSource();
        using var transport = new ConditionalTransport { AfterCommit = cancellation.Cancel };
        using var store = CreateStore(transport);
        using var first = new MemoryStream("published before cancellation"u8.ToArray());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.WriteIfAbsentAsync("exports/key", "application/pdf", first, cancellation.Token));
        transport.AfterCommit = null;
        using var retry = new MemoryStream("replacement"u8.ToArray());
        Assert.Equal(ObjectCreationResult.AlreadyExists,
            await store.WriteIfAbsentAsync("exports/key", "application/pdf", retry, default));
        Assert.Equal("published before cancellation"u8.ToArray(), transport.Objects.Values.Single());
    }

    private static R2ObjectStore CreateStore(HttpMessageHandler transport)
    {
        var options = Options.Create(new R2Options
        {
            ServiceUrl = "https://r2.invalid", AccessKeyId = "test-access", SecretAccessKey = "test-secret", BucketName = "private-scans"
        });
        var sdk = new AmazonS3Client(new BasicAWSCredentials("test-access", "test-secret"), new AmazonS3Config
        {
            ServiceURL = options.Value.ServiceUrl, AuthenticationRegion = "auto", ForcePathStyle = true,
            HttpClientFactory = new TransportFactory(transport), MaxErrorRetry = 0
        });
        return new R2ObjectStore(options, sdk);
    }

    private sealed class TransportFactory(HttpMessageHandler handler) : HttpClientFactory
    {
        public override HttpClient CreateHttpClient(IClientConfig clientConfig) => new(handler, disposeHandler: false);
    }

    // Emulate server-side atomic If-None-Match at the SDK HTTP boundary, not a HEAD check.
    // The first request is deliberately committed after the second has returned success.
    private sealed class ConditionalTransport : HttpMessageHandler
    {
        private int calls;
        public bool DelayFirst { get; init; }
        public Action? AfterCommit { get; set; }
        public HttpStatusCode? FailureStatus { get; init; }
        public string FailureCode { get; init; } = "PreconditionFailed";
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentDictionary<string, byte[]> Objects { get; } = new();
        public ConcurrentQueue<string?> Conditions { get; } = new();
        public ConcurrentQueue<HttpMethod> Methods { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var call = Interlocked.Increment(ref calls);
            var condition = request.Headers.TryGetValues("If-None-Match", out var values) ? values.Single() : null;
            Conditions.Enqueue(condition);
            Methods.Enqueue(request.Method);
            var bytes = await request.Content!.ReadAsByteArrayAsync(ct);
            if (DelayFirst && call == 1)
            {
                FirstStarted.TrySetResult();
                await ReleaseFirst.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            if (FailureStatus is { } failure) return Error(failure, FailureCode);
            var key = request.RequestUri!.AbsolutePath;
            if (condition == "*")
            {
                if (!Objects.TryAdd(key, bytes)) return Error(HttpStatusCode.PreconditionFailed, "PreconditionFailed");
            }
            else Objects[key] = bytes;
            AfterCommit?.Invoke();
            ct.ThrowIfCancellationRequested();
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") };
            response.Headers.ETag = new("\"test-etag\"");
            return response;
        }
        private static HttpResponseMessage Error(HttpStatusCode status, string code) => new(status)
        {
            Content = new StringContent($"<Error><Code>{code}</Code><Message>Injected storage failure</Message></Error>", System.Text.Encoding.UTF8, "application/xml")
        };
    }

    [Fact]
    public async Task CreatePutUrl_UsesConfiguredS3EndpointForLocalMinio()
    {
        var store = new R2ObjectStore(Options.Create(new R2Options
        {
            ServiceUrl = "http://127.0.0.1:9000",
            AccessKeyId = "local-access",
            SecretAccessKey = "local-secret",
            BucketName = "private-scans"
        }));

        var url = await store.CreatePutUrlAsync(
            new PutObjectRequest(
                "quarantine/a-document/an-upload",
                "application/pdf",
                1200,
                DateTimeOffset.UtcNow.AddMinutes(5)),
            CancellationToken.None);

        Assert.Equal("127.0.0.1", url.Host);
        Assert.Equal(9000, url.Port);
        Assert.Equal("http", url.Scheme);
        Assert.Equal("/private-scans/quarantine/a-document/an-upload", url.AbsolutePath);
    }

    [Fact]
    public async Task CreatePutUrl_BindsBucketKeyTypeSizeAndFiveMinuteExpiry()
    {
        var store = new R2ObjectStore(Options.Create(new R2Options
        {
            AccountId = "account123",
            AccessKeyId = "access-key",
            SecretAccessKey = "secret-key",
            BucketName = "private-scans"
        }));
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);

        var url = await store.CreatePutUrlAsync(
            new PutObjectRequest(
                "quarantine/a-document/an-upload",
                "application/pdf",
                1200,
                expiresAt),
            CancellationToken.None);

        Assert.Equal("account123.r2.cloudflarestorage.com", url.Host);
        Assert.Equal("/private-scans/quarantine/a-document/an-upload", url.AbsolutePath);
        Assert.Contains("X-Amz-Expires=300", url.Query, StringComparison.Ordinal);
        Assert.Contains("content-type", Uri.UnescapeDataString(url.Query), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("content-length", Uri.UnescapeDataString(url.Query), StringComparison.OrdinalIgnoreCase);
    }
}
