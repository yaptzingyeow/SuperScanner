using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArksScanner.Application.Abstractions;
using ArksScanner.Domain.Documents;
using ArksScanner.Domain.Ocr;
using ArksScanner.Domain.TextEditing;

namespace ArksScanner.Application.TextEditing;

public sealed record CreateTextEditRequest(
    string OwnerUid,
    Guid DocumentId,
    Guid PageId,
    Guid OcrResultId,
    Guid? ExpectedRevisionId,
    IReadOnlyList<Guid> WordIds,
    string ReplacementText,
    NormalizedBox ReplacementBox,
    TextEditStyle Style,
    string IdempotencyKey);

public sealed record TextEditLimits(bool Enabled, int MaxSelectionWords,
    int MaxReplacementCharacters, double MaxReplacementBoxArea, int MaxQueuedEditsPerPage);

public sealed record TextEditAccepted(Guid EditId, TextEditState State, bool Replayed);
public sealed record LockedTextEditPage(Page Page, PageOcrResult? OcrResult);
public sealed record PreparedTextEdit(string SourceSha256Hex, bool Fits);

public interface ITextEditTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct);
}

public interface ITextEditCommandRepository
{
    Task<ITextEditTransaction> BeginAsync(CancellationToken ct);
    Task<LockedTextEditPage?> FindOwnedForUpdateAsync(string ownerUid,
        Guid documentId, Guid pageId, CancellationToken ct);
    Task<TextEditOperation?> FindByIdempotencyAsync(Guid pageId, string key, CancellationToken ct);
    Task<int> CountPendingAsync(Guid pageId, CancellationToken ct);
    Task<long> NextSequenceAsync(Guid pageId, CancellationToken ct);
    Task AddBaseRevisionAsync(PageRevision revision, CancellationToken ct);
    Task AddEditAsync(TextEditOperation edit, CancellationToken ct);
    Task SaveAsync(CancellationToken ct);
}

public interface ITextEditPreparation
{
    Task<PreparedTextEdit> PrepareAsync(string sourceKey, string replacement,
        NormalizedBox box, TextEditStyle style, CancellationToken ct);
}

public sealed class TextEditDisabledException : Exception;
public sealed class TextEditConflictException : Exception;
public sealed class TextEditValidationException(string code = "text_edit_invalid") : Exception
{
    public string Code { get; } = code;
}

public sealed class CreateTextEdit(
    ITextEditCommandRepository repository,
    ITextEditPreparation preparation,
    IProcessingJobQueue queue,
    IAuditWriter audit,
    IClock clock,
    TextEditLimits limits)
{
    public async Task<TextEditAccepted> HandleAsync(CreateTextEditRequest request, CancellationToken ct)
    {
        if (!limits.Enabled) throw new TextEditDisabledException();
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OwnerUid);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);
        if (request.IdempotencyKey.Length > 128 || request.WordIds is null ||
            request.ReplacementText is null ||
            (request.WordIds.Count == 0 && string.IsNullOrWhiteSpace(request.ReplacementText)) ||
            (request.WordIds.Count == 0 && request.OcrResultId != Guid.Empty) ||
            (request.WordIds.Count != 0 && request.OcrResultId == Guid.Empty) ||
            request.ReplacementText.Length > limits.MaxReplacementCharacters ||
            request.ReplacementBox is null || request.Style is null ||
            request.ReplacementBox.Width * request.ReplacementBox.Height > limits.MaxReplacementBoxArea)
            throw new TextEditValidationException();

        // The hash is derived before reading mutable page state so a retry after completion
        // can return the same operation even though the page revision has advanced.
        var canonical = JsonSerializer.Serialize(new
        {
            request.DocumentId, request.PageId, request.OcrResultId,
            request.ExpectedRevisionId, request.WordIds, request.ReplacementText,
            request.ReplacementBox, request.Style
        });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();

        await using var transaction = await repository.BeginAsync(ct);
        var locked = await repository.FindOwnedForUpdateAsync(request.OwnerUid,
            request.DocumentId, request.PageId, ct)
            ?? throw new TextSelectionNotFoundException();
        var existing = await repository.FindByIdempotencyAsync(request.PageId,
            request.IdempotencyKey, ct);
        if (existing is not null)
        {
            if (existing.CanonicalRequestHash != hash)
                throw new TextEditConflictException();
            await transaction.CommitAsync(ct);
            return new TextEditAccepted(existing.Id, existing.State, true);
        }

        var page = locked.Page;
        var adding = request.WordIds.Count == 0;
        if (page.ActiveRevisionId != request.ExpectedRevisionId ||
            (!adding && (locked.OcrResult is not { State: OcrResultState.Ready } ||
                locked.OcrResult.Id != request.OcrResultId ||
                locked.OcrResult.SourceObjectKey != page.GetProcessedObjectKey())))
            throw new StaleTextSelectionException();
        if (await repository.CountPendingAsync(request.PageId, ct) >= limits.MaxQueuedEditsPerPage)
            throw new TextEditConflictException();

        var selected = adding ? new ValidatedTextSelection([], [], "", request.ReplacementBox) :
            TextSelectionValidator.Validate(new OwnedTextSelection(page.ActiveRevisionId,
                page.GetProcessedObjectKey(), locked.OcrResult!.Id, locked.OcrResult.State,
                locked.OcrResult.SourceObjectKey, locked.OcrResult.Elements.ToArray()),
                request.WordIds, limits.MaxSelectionWords);
        var prepared = await preparation.PrepareAsync(page.GetProcessedObjectKey(),
            request.ReplacementText, request.ReplacementBox, request.Style, ct);
        if (!prepared.Fits) throw new TextEditValidationException("text_edit_overflow");
        if (prepared.SourceSha256Hex.Length != 64 ||
            prepared.SourceSha256Hex.Any(character => !char.IsAsciiHexDigit(character)))
            throw new TextEditValidationException();
        if (page.ActiveRevisionId is not null &&
            (page.ActiveRevision is null ||
             !string.Equals(page.ActiveRevision.Sha256Hex,
                 prepared.SourceSha256Hex, StringComparison.OrdinalIgnoreCase)))
            throw new StaleTextSelectionException();

        var sourceRevisionId = page.ActiveRevisionId;
        if (sourceRevisionId is null)
        {
            var source = PageRevision.CreateBase(Guid.NewGuid(), page.Id,
                page.GetProcessedObjectKey(), prepared.SourceSha256Hex, clock.UtcNow);
            await repository.AddBaseRevisionAsync(source, ct);
            page.ActivateRevision(source);
            sourceRevisionId = source.Id;
        }

        var edit = TextEditOperation.Queue(Guid.NewGuid(), request.DocumentId,
            request.PageId, request.OwnerUid, sourceRevisionId.Value,
            request.OcrResultId, selected.WordIds, selected.OriginalText,
            request.ReplacementText, request.ReplacementBox, request.Style,
            await repository.NextSequenceAsync(request.PageId, ct),
            page.ActiveRevision?.ProducingTextEditId,
            request.IdempotencyKey, hash, "renderer-v2", "layout-v1", clock.UtcNow);
        await repository.AddEditAsync(edit, ct);
        await audit.AppendAsync(new AuditWriteRequest(request.OwnerUid,
            "text_edit.queued", "page", request.PageId,
            JsonSerializer.Serialize(new { editId = edit.Id, pageId = page.Id }),
            clock.UtcNow), ct);
        await queue.EnqueueAsync("RenderTextEdit", edit.Id.ToString(),
            $"text-edit:{edit.Id:N}", ct);
        await repository.SaveAsync(ct);
        await transaction.CommitAsync(ct);
        return new TextEditAccepted(edit.Id, edit.State, false);
    }
}
