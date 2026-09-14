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
    public IReadOnlyCollection<Page> ActivePages => _pages.Where(x => x.RemovedAt is null)
        .OrderBy(x => x.Position)
        .ToArray();
    public long Revision { get; private set; }
    public long PageOrderRevision { get; private set; }

    public static Document Create(Guid id, string ownerFirebaseUid, string title, DateTimeOffset now)
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
        var activePages = ActivePages;
        EnsurePageLimit(maxPages, activePages.Count + 1);

        var page = Page.Create(pageId, Id, pageId, 1, activePages.Count + 1, now);
        _pages.Add(page);
        Status = DocumentStatus.Uploading;
        UpdatedAt = now;
        return page;
    }

    public IReadOnlyList<Page> AppendImportedPages(
        Guid sourceUploadId,
        IReadOnlyList<int> sourcePageIndexes,
        int maxPages,
        DateTimeOffset now)
    {
        if (sourceUploadId == Guid.Empty)
        {
            throw new ArgumentException("A source upload ID is required.", nameof(sourceUploadId));
        }

        ArgumentNullException.ThrowIfNull(sourcePageIndexes);
        if (sourcePageIndexes.Any(index => index < 1))
        {
            throw new ArgumentOutOfRangeException(nameof(sourcePageIndexes));
        }

        var requestedIndexes = sourcePageIndexes.Distinct().ToArray();
        var existingPages = _pages
            .Where(page => page.SourceUploadId == sourceUploadId)
            .ToDictionary(page => page.SourcePageIndex);
        var missingIndexes = requestedIndexes
            .Where(index => !existingPages.ContainsKey(index))
            .ToArray();
        var activePageCount = ActivePages.Count;

        EnsurePageLimit(maxPages, activePageCount + missingIndexes.Length);

        foreach (var sourcePageIndex in missingIndexes)
        {
            var page = Page.Create(
                Guid.NewGuid(),
                Id,
                sourceUploadId,
                sourcePageIndex,
                activePageCount + 1,
                now);
            _pages.Add(page);
            existingPages.Add(sourcePageIndex, page);
            activePageCount++;
        }

        if (missingIndexes.Length > 0)
        {
            if (Status == DocumentStatus.Draft)
            {
                Status = DocumentStatus.Uploading;
            }

            RecordMembershipChange(now);
        }

        return requestedIndexes.Select(index => existingPages[index]).ToArray();
    }

    public void ReorderPages(IReadOnlyList<Guid> pageIds, long expectedPageOrderRevision, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(pageIds);
        if (expectedPageOrderRevision != PageOrderRevision)
        {
            throw new InvalidOperationException("Page order revision is stale.");
        }

        var activePages = ActivePages;
        var activePageIds = activePages.Select(page => page.Id).ToHashSet();
        if (pageIds.Count != activePages.Count ||
            pageIds.Distinct().Count() != pageIds.Count ||
            pageIds.Any(pageId => !activePageIds.Contains(pageId)))
        {
            throw new ArgumentException(
                "Page order must contain every active page exactly once.",
                nameof(pageIds));
        }

        var pagesById = activePages.ToDictionary(page => page.Id);
        for (var index = 0; index < pageIds.Count; index++)
        {
            pagesById[pageIds[index]].SetPosition(index + 1);
        }

        RecordMembershipChange(now);
    }

    public void RemovePage(Guid pageId, string firebaseUid, DateTimeOffset now)
    {
        var page = _pages.SingleOrDefault(candidate => candidate.Id == pageId);
        if (page is null || page.RemovedAt is not null)
        {
            throw new ArgumentException("Page is not an active member of this document.", nameof(pageId));
        }

        page.SoftRemove(firebaseUid, now);
        CompactActivePositions();
        RecordMembershipChange(now);
    }

    public void MarkContentChanged(DateTimeOffset now)
    {
        Revision++;
        UpdatedAt = now;
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

    private static void EnsurePageLimit(int maxPages, int totalPages)
    {
        if (maxPages < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPages));
        }

        if (totalPages > maxPages)
        {
            throw new InvalidOperationException($"Document page limit of {maxPages} reached.");
        }
    }

    private void CompactActivePositions()
    {
        var position = 1;
        foreach (var page in ActivePages)
        {
            page.SetPosition(position++);
        }
    }

    private void RecordMembershipChange(DateTimeOffset now)
    {
        Revision++;
        PageOrderRevision++;
        UpdatedAt = now;
    }
}
