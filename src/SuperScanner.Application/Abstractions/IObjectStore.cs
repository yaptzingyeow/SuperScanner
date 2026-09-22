namespace SuperScanner.Application.Abstractions;

public sealed record PutObjectRequest(
    string ObjectKey,
    string MediaType,
    long SizeBytes,
    DateTimeOffset ExpiresAt);

public sealed record StoredObjectInfo(long SizeBytes, string MediaType, string ETag);

public enum ObjectCreationResult { Created, AlreadyExists }

public interface IObjectStore
{
    Task<Uri> CreatePutUrlAsync(PutObjectRequest request, CancellationToken cancellationToken);
    Task<StoredObjectInfo?> HeadAsync(string objectKey, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken);
    Task PromoteAsync(string quarantineKey, string acceptedKey, CancellationToken cancellationToken);
    Task DeleteAsync(string objectKey, CancellationToken cancellationToken);

    // Atomically create an immutable object. Existing bytes must never be replaced.
    Task<ObjectCreationResult> WriteIfAbsentAsync(string objectKey, string mediaType, Stream content, CancellationToken cancellationToken);

    Task WriteAsync(string objectKey, string mediaType, Stream content, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This object store does not support writes.");
}
