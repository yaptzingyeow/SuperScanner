using SuperScanner.Application.Abstractions;
using SuperScanner.Application.TextEditing;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.Ocr;
using SuperScanner.Domain.TextEditing;

namespace SuperScanner.Application.Tests.TextEditing;

public sealed class CreateTextEditTests
{
    private readonly Guid documentId = Guid.NewGuid();
    private readonly Guid pageId = Guid.NewGuid();
    private readonly Guid ocrId = Guid.NewGuid();
    private readonly Guid wordId = Guid.NewGuid();

    [Fact]
    public async Task Identical_replay_returns_existing_edit_and_one_job()
    {
        var fixture = Fixture();
        var command = Command();
        var first = await fixture.Service.HandleAsync(command, default);
        var second = await fixture.Service.HandleAsync(command, default);
        Assert.Equal(first.EditId, second.EditId);
        Assert.Single(fixture.Queue.Jobs);
        Assert.Single(fixture.Repository.Edits);
    }

    [Fact]
    public async Task Changed_payload_with_same_key_conflicts_without_second_job()
    {
        var fixture = Fixture();
        await fixture.Service.HandleAsync(Command(), default);
        await Assert.ThrowsAsync<TextEditConflictException>(() => fixture.Service.HandleAsync(
            Command() with { ReplacementText = "Different" }, default));
        Assert.Single(fixture.Queue.Jobs);
    }

    [Fact]
    public async Task Stale_revision_is_rejected_before_queueing()
    {
        var fixture = Fixture();
        await Assert.ThrowsAsync<StaleTextSelectionException>(() => fixture.Service.HandleAsync(
            Command() with { ExpectedRevisionId = Guid.NewGuid() }, default));
        Assert.Empty(fixture.Queue.Jobs);
    }

    [Fact]
    public async Task Handwritten_selection_is_rejected_before_image_read()
    {
        var fixture = Fixture(OcrTextType.Handwritten);
        await Assert.ThrowsAsync<UnsupportedTextSelectionException>(() => fixture.Service.HandleAsync(
            Command(), default));
        Assert.Equal(0, fixture.Preparation.CallCount);
    }

    [Fact]
    public async Task Audit_and_job_payload_exclude_document_text()
    {
        var fixture = Fixture();
        var result = await fixture.Service.HandleAsync(Command(), default);
        Assert.Equal(result.EditId.ToString(), fixture.Queue.Jobs.Single().Payload);
        Assert.DoesNotContain("Secret", fixture.Audit.Requests.Single().RegionJson);
        Assert.DoesNotContain("Secret", fixture.Queue.Jobs.Single().Payload);
    }

    [Fact]
    public async Task Disabled_feature_does_not_begin_transaction()
    {
        var fixture = Fixture(enabled: false);
        await Assert.ThrowsAsync<TextEditDisabledException>(() => fixture.Service.HandleAsync(Command(), default));
        Assert.Equal(0, fixture.Repository.TransactionCount);
    }

    [Fact]
    public async Task Unowned_page_is_hidden_before_preparation()
    {
        var fixture = Fixture();
        fixture.Repository.OwnerPresent = false;
        await Assert.ThrowsAsync<TextSelectionNotFoundException>(() => fixture.Service.HandleAsync(Command(), default));
        Assert.Equal(0, fixture.Preparation.CallCount);
    }

    [Fact]
    public async Task Wrong_ocr_result_is_stale()
    {
        var fixture = Fixture();
        await Assert.ThrowsAsync<StaleTextSelectionException>(() => fixture.Service.HandleAsync(
            Command() with { OcrResultId = Guid.NewGuid() }, default));
        Assert.Empty(fixture.Queue.Jobs);
    }

    [Fact]
    public async Task Queue_depth_limit_rejects_another_edit()
    {
        var fixture = Fixture();
        fixture.Repository.PendingCount = 2;
        await Assert.ThrowsAsync<TextEditConflictException>(() => fixture.Service.HandleAsync(Command(), default));
        Assert.Empty(fixture.Queue.Jobs);
    }

    [Fact]
    public async Task Overflow_rejects_without_edit_or_job()
    {
        var fixture = Fixture();
        fixture.Preparation.Fits = false;
        await Assert.ThrowsAsync<TextEditValidationException>(() => fixture.Service.HandleAsync(Command(), default));
        Assert.Empty(fixture.Repository.Edits);
        Assert.Empty(fixture.Queue.Jobs);
    }

    [Fact]
    public async Task Unavailable_font_rejects_without_edit_or_job()
    {
        var fixture = Fixture();
        fixture.Preparation.FontAvailable = false;
        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Service.HandleAsync(Command(), default));
        Assert.Empty(fixture.Repository.Edits);
        Assert.Empty(fixture.Queue.Jobs);
    }

    [Fact]
    public async Task Overlapping_unselected_word_is_rejected_before_preparation()
    {
        var fixture = Fixture(overlap: true);
        await Assert.ThrowsAsync<InvalidTextSelectionException>(() => fixture.Service.HandleAsync(Command(), default));
        Assert.Equal(0, fixture.Preparation.CallCount);
    }

    [Fact]
    public async Task Derived_source_hash_mismatch_is_rejected_without_queueing()
    {
        var fixture = Fixture(existingRevision: true);
        var expectedRevision = fixture.Repository.Page.ActiveRevisionId;
        await Assert.ThrowsAsync<StaleTextSelectionException>(() => fixture.Service.HandleAsync(
            Command() with { ExpectedRevisionId = expectedRevision }, default));
        Assert.Empty(fixture.Queue.Jobs);
    }

    [Fact]
    public async Task Edit_from_derived_revision_links_to_producing_edit()
    {
        var fixture = Fixture(derivedSource: true);
        var parentEditId = fixture.Repository.Page.ActiveRevision!.ProducingTextEditId;
        await fixture.Service.HandleAsync(Command() with
        {
            ExpectedRevisionId = fixture.Repository.Page.ActiveRevisionId
        }, default);
        Assert.Equal(parentEditId, fixture.Repository.Edits.Single().BranchParentEditId);
    }

    private CreateTextEditRequest Command() => new("owner", documentId, pageId, ocrId,
        null, [wordId], "Secret", new NormalizedBox(0.1, 0.2, 0.3, 0.1),
        new TextEditStyle("noto-sans", "archive-main-regular", 0.04, 400,
            "#000000", 0, 0.25, 0, TextAlignment.Left), "test-key");

    private FixtureData Fixture(OcrTextType textType = OcrTextType.Printed,
        bool enabled = true, bool overlap = false, bool existingRevision = false,
        bool derivedSource = false)
    {
        var document = Document.Create(documentId, "owner", "Document", DateTimeOffset.UtcNow);
        var page = document.AddPage(pageId, 10, DateTimeOffset.UtcNow);
        page.MarkImportReady("original.jpg", "image/jpeg");
        page.SetPreview("preview.jpg", "thumb.jpg");
        page.MarkReady();
        if (existingRevision)
            page.ActivateRevision(PageRevision.CreateBase(Guid.NewGuid(), pageId,
                "derived.jpg", new string('a', 64), DateTimeOffset.UtcNow));
        if (derivedSource)
            page.ActivateRevision(PageRevision.CreateDerived(Guid.NewGuid(), pageId,
                Guid.NewGuid(), Guid.NewGuid(), "derived.jpg", new string('b', 64),
                DateTimeOffset.UtcNow));
        var result = PageOcrResult.Queue(ocrId, pageId, page.GetProcessedObjectKey(), new string('a', 64),
            "en", DateTimeOffset.UtcNow);
        result.BeginAttempt(1, DateTimeOffset.UtcNow);
        var lineId = Guid.NewGuid();
        var line = OcrElement.Create(lineId, ocrId, null, OcrElementKind.Line,
            "Name", 0.9, textType, 0,
            [new(0.1, 0.2), new(0.4, 0.2), new(0.4, 0.3), new(0.1, 0.3)]);
        var word = OcrElement.Create(wordId, ocrId, lineId, OcrElementKind.Word,
            "Name", 0.9, textType, 0,
            [new(0.1, 0.2), new(0.4, 0.2), new(0.4, 0.3), new(0.1, 0.3)]);
        var elements = new List<OcrElement> { line, word };
        if (overlap)
        {
            var otherLineId = Guid.NewGuid();
            elements.Add(OcrElement.Create(otherLineId, ocrId, null, OcrElementKind.Line,
                "Other", .9, OcrTextType.Printed, 1,
                [new(.2, .22), new(.3, .22), new(.3, .28), new(.2, .28)]));
            elements.Add(OcrElement.Create(Guid.NewGuid(), ocrId, otherLineId,
                OcrElementKind.Word, "Other", .9, OcrTextType.Printed, 0,
                [new(.2, .22), new(.3, .22), new(.3, .28), new(.2, .28)]));
        }
        result.Complete("test", "v1", "Name", elements, DateTimeOffset.UtcNow);
        var repository = new FakeRepository(page, result);
        var preparation = new FakePreparation();
        var queue = new FakeQueue();
        var audit = new FakeAudit();
        var service = new CreateTextEdit(repository, preparation, queue, audit,
            new FakeClock(), new TextEditLimits(enabled, 50, 4000, 0.5, 2));
        return new(service, repository, preparation, queue, audit);
    }

    private sealed record FixtureData(CreateTextEdit Service, FakeRepository Repository,
        FakePreparation Preparation, FakeQueue Queue, FakeAudit Audit);

    private sealed class FakeRepository(Page page, PageOcrResult ocr) : ITextEditCommandRepository
    {
        public Page Page => page;
        public List<TextEditOperation> Edits { get; } = [];
        public bool OwnerPresent { get; set; } = true;
        public int PendingCount { get; set; }
        public int TransactionCount { get; private set; }
        public Task<ITextEditTransaction> BeginAsync(CancellationToken ct) =>
            Task.FromResult<ITextEditTransaction>(Begin());
        private ITextEditTransaction Begin()
        {
            TransactionCount++;
            return new FakeTransaction();
        }
        public Task<LockedTextEditPage?> FindOwnedForUpdateAsync(string ownerUid, Guid documentId,
            Guid pageId, CancellationToken ct) => Task.FromResult<LockedTextEditPage?>(
                OwnerPresent && ownerUid == "owner" && page.Id == pageId && page.DocumentId == documentId
                    ? new LockedTextEditPage(page, ocr) : null);
        public Task<TextEditOperation?> FindByIdempotencyAsync(Guid pageId, string key, CancellationToken ct) =>
            Task.FromResult(Edits.SingleOrDefault(edit => edit.PageId == pageId && edit.IdempotencyKey == key));
        public Task<int> CountPendingAsync(Guid pageId, CancellationToken ct) =>
            Task.FromResult(PendingCount + Edits.Count(edit => edit.State is TextEditState.Queued or TextEditState.Processing));
        public Task<long> NextSequenceAsync(Guid pageId, CancellationToken ct) =>
            Task.FromResult((long)Edits.Count + 1);
        public Task AddBaseRevisionAsync(PageRevision revision, CancellationToken ct) => Task.CompletedTask;
        public Task AddEditAsync(TextEditOperation edit, CancellationToken ct)
        {
            Edits.Add(edit);
            return Task.CompletedTask;
        }
        public Task SaveAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeTransaction : ITextEditTransaction
    {
        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakePreparation : ITextEditPreparation
    {
        public int CallCount { get; private set; }
        public bool Fits { get; set; } = true;
        public bool FontAvailable { get; set; } = true;
        public Task<PreparedTextEdit> PrepareAsync(string sourceKey, string replacement,
            NormalizedBox box, TextEditStyle style, CancellationToken ct)
        {
            CallCount++;
            if (!FontAvailable) throw new KeyNotFoundException();
            return Task.FromResult(new PreparedTextEdit(new string('b', 64), Fits));
        }
    }

    private sealed class FakeQueue : IProcessingJobQueue
    {
        public List<(string Type, string Payload, string Key)> Jobs { get; } = [];
        public Task EnqueueAsync(string type, string payload, string key, CancellationToken ct)
        {
            Jobs.Add((type, payload, key));
            return Task.CompletedTask;
        }
        public Task<ProcessingJobLease?> TryLeaseAsync(string id, TimeSpan duration, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> HeartbeatAsync(Guid id, string worker, TimeSpan duration, CancellationToken ct) => throw new NotSupportedException();
        public Task CompleteAsync(Guid id, string worker, CancellationToken ct) => throw new NotSupportedException();
        public Task RescheduleAsync(Guid id, string worker, string code, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeAudit : IAuditWriter
    {
        public List<AuditWriteRequest> Requests { get; } = [];
        public Task<Guid> AppendAsync(AuditWriteRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(Guid.NewGuid());
        }
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 23, 0, 0, 0, TimeSpan.Zero);
    }
}
