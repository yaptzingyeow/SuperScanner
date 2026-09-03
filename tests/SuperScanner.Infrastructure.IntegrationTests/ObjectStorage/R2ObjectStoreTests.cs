using Microsoft.Extensions.Options;
using SuperScanner.Application.Abstractions;
using SuperScanner.Infrastructure.ObjectStorage;

namespace SuperScanner.Infrastructure.IntegrationTests.ObjectStorage;

public sealed class R2ObjectStoreTests
{
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
