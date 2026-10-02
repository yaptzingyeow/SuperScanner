using ArksScanner.Application.Abstractions;
using ArksScanner.Application.TextEditing;
using ArksScanner.Domain.Ocr;

namespace ArksScanner.Infrastructure.TextEditing;

// Renders a private, non-persisted preview. The write path repeats all checks
// against the current revision when the user confirms the change.
public sealed class TextEditPreview(
    ITextSelectionRepository selections,
    IObjectStore store,
    ITextEditRenderer renderer,
    TextEditLimits limits)
{
    public async Task<TextEditRenderResult> RenderAsync(CreateTextEditRequest request,
        CancellationToken ct)
    {
        if (!limits.Enabled) throw new TextEditDisabledException();
        if (request.WordIds is null || request.ReplacementBox is null ||
            request.Style is null || request.ReplacementText is null ||
            (request.WordIds.Count == 0 && (request.OcrResultId != Guid.Empty ||
                string.IsNullOrWhiteSpace(request.ReplacementText))) ||
            (request.WordIds.Count != 0 && request.OcrResultId == Guid.Empty) ||
            request.ReplacementText.Length > limits.MaxReplacementCharacters ||
            request.ReplacementBox.Width * request.ReplacementBox.Height >
                limits.MaxReplacementBoxArea)
            throw new TextEditValidationException();

        var selection = await selections.FindOwnedAsync(request.OwnerUid,
            request.DocumentId, request.PageId, request.OcrResultId, ct)
            ?? throw new TextSelectionNotFoundException();
        if (selection.ActiveRevisionId != request.ExpectedRevisionId ||
            selection.OcrResultId != request.OcrResultId ||
            selection.OcrState != OcrResultState.Ready ||
            selection.SourceObjectKey != selection.OcrSourceObjectKey)
            throw new StaleTextSelectionException();
        var selected = request.WordIds.Count == 0
            ? new ValidatedTextSelection([], [], "", request.ReplacementBox)
            : TextSelectionValidator.Validate(selection, request.WordIds,
                limits.MaxSelectionWords);
        var selectedIds = selected.WordIds.ToHashSet();
        var protectedPolygons = selection.Elements
            .Where(element => element.Kind == OcrElementKind.Word &&
                !selectedIds.Contains(element.Id))
            .Select(element => element.Polygon).ToArray();

        await using var source = await store.OpenReadAsync(selection.SourceObjectKey, ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > 25_000_000)
                throw new TextEditValidationException();
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }
        return await renderer.RenderAsync(new TextEditRenderRequest(buffer.ToArray(),
            selected.Words.Select(word => word.Polygon).ToArray(), protectedPolygons,
            request.ReplacementBox, request.ReplacementText, request.Style,
            TextEditRenderer.RendererVersion, TextLayoutEngine.LayoutVersion), ct);
    }
}
