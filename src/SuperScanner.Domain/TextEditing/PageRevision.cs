namespace SuperScanner.Domain.TextEditing;

public sealed class PageRevision
{
    private PageRevision()
    {
    }

    public Guid Id { get; private set; }
    public Guid PageId { get; private set; }
    public Guid? ParentRevisionId { get; private set; }
    public Guid? ProducingTextEditId { get; private set; }
    public string ObjectKey { get; private set; } = string.Empty;
    public string MediaType { get; private set; } = "image/jpeg";
    public string Sha256Hex { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    public static PageRevision CreateBase(
        Guid id,
        Guid pageId,
        string objectKey,
        string sha256Hex,
        DateTimeOffset createdAt) =>
        Create(id, pageId, null, null, objectKey, sha256Hex, createdAt);

    public static PageRevision CreateDerived(
        Guid id,
        Guid pageId,
        Guid parentRevisionId,
        Guid producingTextEditId,
        string objectKey,
        string sha256Hex,
        DateTimeOffset createdAt)
    {
        if (parentRevisionId == Guid.Empty)
            throw new ArgumentException("A parent revision ID is required.", nameof(parentRevisionId));
        if (producingTextEditId == Guid.Empty)
            throw new ArgumentException("A producing text edit ID is required.", nameof(producingTextEditId));

        return Create(
            id,
            pageId,
            parentRevisionId,
            producingTextEditId,
            objectKey,
            sha256Hex,
            createdAt);
    }

    private static PageRevision Create(
        Guid id,
        Guid pageId,
        Guid? parentRevisionId,
        Guid? producingTextEditId,
        string objectKey,
        string sha256Hex,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty) throw new ArgumentException("A revision ID is required.", nameof(id));
        if (pageId == Guid.Empty) throw new ArgumentException("A page ID is required.", nameof(pageId));
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256Hex);

        return new PageRevision
        {
            Id = id,
            PageId = pageId,
            ParentRevisionId = parentRevisionId,
            ProducingTextEditId = producingTextEditId,
            ObjectKey = objectKey,
            Sha256Hex = sha256Hex,
            CreatedAt = createdAt
        };
    }
}
