using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using ArksScanner.Application.Abstractions;
using ArksScanner.Application.Ocr;
using ArksScanner.Domain.Documents;
using ArksScanner.Domain.Ocr;
using ArksScanner.Domain.TextEditing;
using ArksScanner.Infrastructure.Persistence;

namespace ArksScanner.Infrastructure.TextEditing;

public sealed class TextEditProcessingException(string safeCode, bool retryable) : Exception
{
    public string SafeCode { get; } = safeCode;
    public bool Retryable { get; } = retryable;
}

public interface ITextEditProcessor
{
    Task RunAsync(Guid editId, CancellationToken ct);
    Task FailAsync(Guid editId, string safeCode, CancellationToken ct);
}

public sealed class TextEditProcessor(
    AppDbContext db,
    IObjectStore store,
    ITextEditRenderer renderer,
    IClock clock) : ITextEditProcessor
{
    public async Task RunAsync(Guid editId, CancellationToken ct)
    {
        if (editId == Guid.Empty)
            throw new TextEditProcessingException("text_edit_invalid_job", false);
        var edit = await db.TextEditOperations.SingleOrDefaultAsync(candidate =>
            candidate.Id == editId, ct)
            ?? throw new TextEditProcessingException("text_edit_invalid_job", false);
        if (edit.State is TextEditState.Succeeded or TextEditState.Failed) return;
        var page = await db.Pages.Include(candidate => candidate.ActiveRevision)
            .SingleOrDefaultAsync(candidate => candidate.Id == edit.PageId, ct)
            ?? throw new TextEditProcessingException("text_edit_stale_revision", false);
        if (page.RemovedAt is not null || page.State != PageState.Ready ||
            page.ActiveRevisionId != edit.SourceRevisionId ||
            page.ActiveRevision is null || page.ActiveRevision.Id != edit.SourceRevisionId)
            throw new TextEditProcessingException("text_edit_stale_revision", false);
        var sourceRevision = page.ActiveRevision;
        var sourceObjectKey = sourceRevision.ObjectKey;
        var adding = edit.SelectedOcrElementIds.Count == 0;
        var ocr = adding ? null : await db.PageOcrResults.Include(result => result.Elements)
            .SingleOrDefaultAsync(result => result.Id == edit.SourceOcrResultId &&
                result.PageId == page.Id, ct);
        if (!adding && (ocr is null || ocr.State != OcrResultState.Ready ||
            ocr.SourceObjectKey != sourceRevision.ObjectKey))
            throw new TextEditProcessingException("text_edit_stale_ocr", false);
        var selectedIds = edit.SelectedOcrElementIds.ToHashSet();
        var selected = (ocr?.Elements ?? []).Where(element => selectedIds.Contains(element.Id) &&
            element.Kind == OcrElementKind.Word && element.TextType == OcrTextType.Printed)
            .ToArray();
        if (selected.Length != selectedIds.Count)
            throw new TextEditProcessingException("text_edit_stale_ocr", false);
        var protectedPolygons = (ocr?.Elements ?? []).Where(element =>
            element.Kind == OcrElementKind.Word && !selectedIds.Contains(element.Id))
            .Select(element => element.Polygon).ToArray();

        if (edit.State == TextEditState.Queued)
        {
            edit.Start(clock.UtcNow);
            await db.SaveChangesAsync(ct);
        }

        byte[] sourceBytes;
        try
        {
            await using var stream = await store.OpenReadAsync(sourceRevision.ObjectKey, ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + read > 25_000_000)
                    throw new TextEditProcessingException("text_edit_source_invalid", false);
                await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
            }
            sourceBytes = buffer.ToArray();
        }
        catch (FileNotFoundException)
        {
            throw new TextEditProcessingException("text_edit_source_missing", false);
        }
        if (!string.Equals(Hash(sourceBytes), sourceRevision.Sha256Hex,
            StringComparison.OrdinalIgnoreCase))
            throw new TextEditProcessingException("text_edit_source_mismatch", false);

        var result = await renderer.RenderAsync(new TextEditRenderRequest(sourceBytes,
            selected.Select(element => element.Polygon).ToArray(), protectedPolygons,
            edit.ReplacementBox, edit.ReplacementText, edit.Style,
            edit.RendererVersion, edit.LayoutVersion), ct);
        if (result.FailureCode is not null)
            throw new TextEditProcessingException(result.FailureCode, false);
        if (result.Output is null || result.Sha256Hex is null ||
            !string.Equals(Hash(result.Output), result.Sha256Hex,
                StringComparison.OrdinalIgnoreCase))
            throw new TextEditProcessingException("text_edit_render_invalid", false);

        var key = $"page-revisions/{edit.DocumentId:N}/{edit.PageId:N}/{edit.Id:N}.png";
        await using (var output = new MemoryStream(result.Output, writable: false))
        {
            var creation = await store.WriteIfAbsentAsync(key, "image/png", output, ct);
            if (creation == ObjectCreationResult.AlreadyExists)
            {
                await using var existing = await store.OpenReadAsync(key, ct);
                using var buffer = new MemoryStream();
                await existing.CopyToAsync(buffer, ct);
                if (!CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(buffer.ToArray()), SHA256.HashData(result.Output)))
                    throw new TextEditProcessingException("text_edit_object_conflict", false);
            }
        }

        // Discard the pre-render EF snapshot. The page lock is acquired only after
        // rendering and object storage; activation is one short transaction.
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var currentPage = await db.Pages.FromSqlInterpolated($"""
                SELECT * FROM pages WHERE "Id" = {edit.PageId} FOR UPDATE
                """)
            .Include(candidate => candidate.ActiveRevision)
            .SingleOrDefaultAsync(ct)
            ?? throw new TextEditProcessingException("text_edit_stale_revision", false);
        var currentEdit = await db.TextEditOperations.SingleAsync(candidate =>
            candidate.Id == editId, ct);
        if (currentEdit.State == TextEditState.Succeeded)
        {
            await transaction.CommitAsync(ct);
            return;
        }
        if (currentPage.RemovedAt is not null || currentPage.State != PageState.Ready ||
            currentPage.ActiveRevisionId != currentEdit.SourceRevisionId ||
            currentEdit.State == TextEditState.Failed)
            throw new TextEditProcessingException("text_edit_stale_revision", false);
        var revision = PageRevision.CreateDerived(Guid.NewGuid(), currentPage.Id,
            currentEdit.SourceRevisionId, currentEdit.Id, key, result.Sha256Hex,
            clock.UtcNow);
        db.PageRevisions.Add(revision);
        currentPage.ActivateRevision(revision);
        await CarryOcrForwardAsync(currentEdit, currentPage.Id, sourceObjectKey, key, ct);
        var document = await db.Documents.SingleAsync(candidate =>
            candidate.Id == currentPage.DocumentId, ct);
        document.MarkContentChanged(clock.UtcNow);
        currentEdit.Complete(revision.Id, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    /// <summary>
    /// Gives the new revision the source revision's OCR minus the edited words plus the typed
    /// text, so the next edit needs no new (paid) recognition. Skipped when the source page was
    /// never recognized; the user can still ask for recognition later.
    /// </summary>
    private async Task CarryOcrForwardAsync(
        TextEditOperation edit, Guid pageId, string sourceObjectKey, string revisionKey, CancellationToken ct)
    {
        var source = await db.PageOcrResults.Include(result => result.Elements)
            .Where(result => result.PageId == pageId && result.SourceObjectKey == sourceObjectKey &&
                result.State == OcrResultState.Ready)
            .OrderByDescending(result => result.Id == edit.SourceOcrResultId)
            .ThenByDescending(result => result.CompletedAt)
            .FirstOrDefaultAsync(ct);
        if (source is null) return;
        var fingerprint = OcrSourceFingerprint.Create(revisionKey);
        if (await db.PageOcrResults.AnyAsync(result => result.PageId == pageId &&
                result.SourceFingerprint == fingerprint, ct))
            return;

        var now = clock.UtcNow;
        var carried = PageOcrResult.Queue(Guid.NewGuid(), pageId, revisionKey, fingerprint, source.Language, now);
        var built = OcrCarryForward.Build(carried.Id, source.Elements,
            edit.SelectedOcrElementIds.ToHashSet(), edit.ReplacementBox, edit.ReplacementText);
        carried.BeginAttempt(1, now);
        carried.Complete(source.ProviderName ?? "Unknown", OcrCarryForward.ModelVersion,
            built.FullText, built.Elements, now);
        db.PageOcrResults.Add(carried);
        db.OcrElements.AddRange(built.Elements);
    }

    public async Task FailAsync(Guid editId, string safeCode, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var edit = await db.TextEditOperations.SingleOrDefaultAsync(candidate =>
            candidate.Id == editId, ct);
        if (edit is null || edit.State is TextEditState.Succeeded or TextEditState.Failed) return;
        edit.Fail(safeCode, clock.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
