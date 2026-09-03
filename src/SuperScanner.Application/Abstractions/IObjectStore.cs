namespace SuperScanner.Application.Abstractions;

public sealed record PutObjectRequest(
    string ObjectKey,
    string MediaType,
    long SizeBytes,
    DateTimeOffset ExpiresAt);

public sealed record StoredObjectInfo(long SizeBytes, string MediaType, string ETag);

public interface IObjectStore
{
    Task<Uri> CreatePutUrlAsync(PutObjectRequest request, CancellationToken cancellationToken);
    Task<StoredObjectInfo?> HeadAsync(string objectKey, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken);
    Task PromoteAsync(string quarantineKey, string acceptedKey, CancellationToken cancellationToken);
    Task DeleteAsync(string objectKey, CancellationToken cancellationToken);
}
