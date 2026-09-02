namespace SuperScanner.Domain.Documents;

public sealed class Page
{
    private Page()
    {
    }

    public Guid Id { get; private set; }

    public Guid DocumentId { get; private set; }

    public int PageNumber { get; private set; }

    public string? OriginalObjectKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    internal static Page Create(Guid id, Guid documentId, int pageNumber, DateTimeOffset now) =>
        new()
        {
            Id = id,
            DocumentId = documentId,
            PageNumber = pageNumber,
            CreatedAt = now
        };

    public void AcceptOriginal(string objectKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        if (OriginalObjectKey is not null)
        {
            throw new InvalidOperationException("Original asset is immutable.");
        }

        OriginalObjectKey = objectKey;
    }
}
