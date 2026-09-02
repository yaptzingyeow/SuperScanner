namespace SuperScanner.Domain.Documents;

public sealed class Document
{
    private readonly List<Page> _pages = [];

    private Document()
    {
    }

    public Guid Id { get; private set; }

    public string OwnerFirebaseUid { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public DocumentStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<Page> Pages => _pages;

    public static Document Create(
        Guid id,
        string ownerFirebaseUid,
        string title,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerFirebaseUid);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        return new Document
        {
            Id = id,
            OwnerFirebaseUid = ownerFirebaseUid,
            Title = title.Trim(),
            Status = DocumentStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public Page AddPage(Guid pageId, int maxPages, DateTimeOffset now)
    {
        if (maxPages < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPages));
        }

        if (_pages.Count >= maxPages)
        {
            throw new InvalidOperationException($"Document page limit of {maxPages} reached.");
        }

        var page = Page.Create(pageId, Id, _pages.Count + 1, now);
        _pages.Add(page);
        Status = DocumentStatus.Uploading;
        UpdatedAt = now;
        return page;
    }

    public void MarkProcessing(DateTimeOffset now)
    {
        Status = DocumentStatus.Processing;
        UpdatedAt = now;
    }

    public void MarkReady(DateTimeOffset now)
    {
        Status = DocumentStatus.Ready;
        UpdatedAt = now;
    }
}
