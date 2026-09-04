using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using SuperScanner.Application.Abstractions;

namespace SuperScanner.Infrastructure.ObjectStorage;

public sealed class R2ObjectStore : IObjectStore, IDisposable
{
    private readonly IAmazonS3 _client;
    private readonly string _bucketName;
    private readonly Protocol _presignedUrlProtocol;

    public R2ObjectStore(IOptions<R2Options> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var value = options.Value;
        if (string.IsNullOrWhiteSpace(value.ServiceUrl))
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value.AccountId);
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(value.AccessKeyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(value.SecretAccessKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(value.BucketName);

        _bucketName = value.BucketName;
        var isCustomEndpoint = !string.IsNullOrWhiteSpace(value.ServiceUrl);
        var customEndpoint = isCustomEndpoint ? new Uri(value.ServiceUrl) : null;
        _presignedUrlProtocol = customEndpoint?.Scheme == Uri.UriSchemeHttp
            ? Protocol.HTTP
            : Protocol.HTTPS;
        _client = new AmazonS3Client(
            new BasicAWSCredentials(value.AccessKeyId, value.SecretAccessKey),
            new AmazonS3Config
            {
                ServiceURL = isCustomEndpoint
                    ? value.ServiceUrl
                    : $"https://{value.AccountId}.r2.cloudflarestorage.com",
                AuthenticationRegion = isCustomEndpoint ? "us-east-1" : "auto",
                UseHttp = customEndpoint?.Scheme == Uri.UriSchemeHttp,
                ForcePathStyle = true
            });
    }

    public async Task<Uri> CreatePutUrlAsync(
        SuperScanner.Application.Abstractions.PutObjectRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var presignRequest = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = request.ObjectKey,
            Verb = HttpVerb.PUT,
            Protocol = _presignedUrlProtocol,
            Expires = request.ExpiresAt.UtcDateTime,
            ContentType = request.MediaType
        };
        presignRequest.Headers.ContentLength = request.SizeBytes;

        var url = await _client.GetPreSignedURLAsync(presignRequest);
        return new Uri(url);
    }

    public async Task<StoredObjectInfo?> HeadAsync(
        string objectKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.GetObjectMetadataAsync(
                new GetObjectMetadataRequest { BucketName = _bucketName, Key = objectKey },
                cancellationToken);
            return new StoredObjectInfo(
                response.ContentLength,
                response.Headers.ContentType,
                response.ETag);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<Stream> OpenReadAsync(
        string objectKey,
        CancellationToken cancellationToken)
    {
        var response = await _client.GetObjectAsync(
            new GetObjectRequest { BucketName = _bucketName, Key = objectKey },
            cancellationToken);
        return new ResponseOwnedStream(response);
    }

    public async Task PromoteAsync(
        string quarantineKey,
        string acceptedKey,
        CancellationToken cancellationToken)
    {
        await _client.CopyObjectAsync(
            new CopyObjectRequest
            {
                SourceBucket = _bucketName,
                SourceKey = quarantineKey,
                DestinationBucket = _bucketName,
                DestinationKey = acceptedKey
            },
            cancellationToken);
        await DeleteAsync(quarantineKey, cancellationToken);
    }

    public async Task DeleteAsync(string objectKey, CancellationToken cancellationToken) =>
        await _client.DeleteObjectAsync(
            new DeleteObjectRequest { BucketName = _bucketName, Key = objectKey },
            cancellationToken);

    public void Dispose() => _client.Dispose();

    private sealed class ResponseOwnedStream(GetObjectResponse response) : Stream
    {
        private readonly Stream _inner = response.ResponseStream;

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            _inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                response.Dispose();
            }

            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            response.Dispose();
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }
    }
}
