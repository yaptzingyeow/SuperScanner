using Microsoft.Extensions.Options;
using ArksScanner.Api.Auth;
using ArksScanner.Application.TextEditing;
using ArksScanner.Infrastructure.TextEditing;
using ArksScanner.Domain.TextEditing;

namespace ArksScanner.Api.Endpoints;

public static class TextEditingEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/text-edit-fonts", (IFontCatalogue catalogue, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "private, max-age=300";
            return Results.Ok(catalogue.Entries
                .Where(face => face.Enabled && face.SelectableForNewEdits)
                .OrderBy(face => face.Category)
                .ThenBy(face => face.FamilyName, StringComparer.Ordinal)
                .ThenBy(face => face.Weight)
                .ThenBy(face => face.Style)
                .Select(face => new FontCatalogueDto(
                    face.CatalogueId, face.Version, face.DisplayName, face.FamilyName,
                    face.Category.ToString(), face.Weight, face.Style.ToString(),
                    face.WebFamilyName, "/" + face.WebAssetPath.Replace('\\', '/'), true)));
        }).RequireAuthorization();

        var group = endpoints
            .MapGroup("/api/documents/{documentId:guid}/pages/{pageId:guid}/text-edits")
            .RequireAuthorization(AuthPolicies.SignedInAccount);
        group.MapPost("/style-proposal", ProposeAsync);
        group.MapPost("/preview", PreviewAsync);
        group.MapPost("", ApplyAsync);
        group.MapGet("/history", HistoryAsync);
        group.MapPost("/undo", (Guid documentId, Guid pageId,
            SwitchRevisionRequest request, ICurrentUser user, SwitchPageRevision command,
            GetPageEditHistory history,
            IOptions<TextEditingOptions> options, HttpContext context, CancellationToken ct) =>
            SwitchAsync(documentId, pageId, request, RevisionSwitchDirection.Undo,
                user, command, history, options, context, ct));
        group.MapPost("/redo", (Guid documentId, Guid pageId,
            SwitchRevisionRequest request, ICurrentUser user, SwitchPageRevision command,
            GetPageEditHistory history,
            IOptions<TextEditingOptions> options, HttpContext context, CancellationToken ct) =>
            SwitchAsync(documentId, pageId, request, RevisionSwitchDirection.Redo,
                user, command, history, options, context, ct));
        group.MapGet("/{editId:guid}", GetAsync);
    }

    public sealed record FontCatalogueDto(string CatalogueId, string Version,
        string DisplayName, string FamilyName, string Category, int Weight,
        string Style, string WebFamilyName, string WebAssetUrl, bool Enabled);

    private static async Task<IResult> ProposeAsync(
        Guid documentId,
        Guid pageId,
        StyleProposalRequest request,
        ICurrentUser user,
        ProposeTextStyle proposal,
        IOptions<TextEditingOptions> options,
        HttpContext context,
        CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!options.Value.Enabled)
            return Results.Json(new { code = "text_edit_disabled" },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        if (request.OcrResultId == Guid.Empty || request.WordIds is null || request.WordIds.Length == 0)
            return Results.UnprocessableEntity(new { code = "text_selection_invalid" });

        try
        {
            var result = await proposal.HandleAsync(user.FirebaseUid, documentId,
                pageId, request.OcrResultId, request.WordIds, ct);
            return Results.Ok(result);
        }
        catch (TextSelectionNotFoundException)
        {
            return Results.NotFound();
        }
        catch (StaleTextSelectionException)
        {
            return Results.Conflict(new { code = "text_selection_stale" });
        }
        catch (UnsupportedTextSelectionException)
        {
            return Results.UnprocessableEntity(new { code = "text_selection_unsupported" });
        }
        catch (InvalidTextSelectionException)
        {
            return Results.UnprocessableEntity(new { code = "text_selection_invalid" });
        }
    }

    public sealed record StyleProposalRequest(Guid OcrResultId, Guid[]? WordIds);
    public sealed record SwitchRevisionRequest(Guid? ExpectedRevisionId);

    private static async Task<IResult> SwitchAsync(Guid documentId, Guid pageId,
        SwitchRevisionRequest request, RevisionSwitchDirection direction,
        ICurrentUser user, SwitchPageRevision command, GetPageEditHistory history,
        IOptions<TextEditingOptions> options, HttpContext context, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!options.Value.Enabled)
            return Results.Json(new { code = "text_edit_disabled" },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        try
        {
            await command.HandleAsync(new SwitchPageRevisionRequest(
                user.FirebaseUid, documentId, pageId, request.ExpectedRevisionId,
                direction), ct);
            return Results.Ok(await history.HandleAsync(user.FirebaseUid,
                documentId, pageId, ct));
        }
        catch (TextSelectionNotFoundException) { return Results.NotFound(); }
        catch (TextRevisionConflictException)
        {
            return Results.Conflict(new { code = "text_revision_conflict" });
        }
        catch (TextRevisionBoundaryException)
        {
            return Results.UnprocessableEntity(new { code = "text_revision_boundary" });
        }
    }

    public sealed record ApplyTextEditRequest(Guid OcrResultId,
        Guid? ExpectedRevisionId, Guid[]? WordIds, string? ReplacementText,
        NormalizedBox? ReplacementBox, TextEditStyle? Style, string? IdempotencyKey);

    private static async Task<IResult> PreviewAsync(Guid documentId, Guid pageId,
        ApplyTextEditRequest request, ICurrentUser user, TextEditPreview preview,
        IOptions<TextEditingOptions> options, HttpContext context, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!options.Value.Enabled)
            return Results.Json(new { code = "text_edit_disabled" },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        if (request.WordIds is null ||
            request.ReplacementText is null || request.ReplacementBox is null ||
            request.Style is null)
            return Results.UnprocessableEntity(new { code = "text_edit_invalid" });
        try
        {
            var result = await preview.RenderAsync(new CreateTextEditRequest(
                user.FirebaseUid, documentId, pageId, request.OcrResultId,
                request.ExpectedRevisionId, request.WordIds, request.ReplacementText,
                request.ReplacementBox, request.Style, "preview"), ct);
            if (result.DiagnosticReason is not null)
                context.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("TextEditPreviewDiagnostics")
                    .LogWarning("Text edit preview rejected for page {PageId}: {Code}, {Reason}",
                        pageId, result.FailureCode, result.DiagnosticReason);
            return result.FailureCode is null && result.Output is not null
                ? Results.File(result.Output, "image/png")
                : Results.UnprocessableEntity(new { code = result.FailureCode ?? "text_edit_render_invalid" });
        }
        catch (TextSelectionNotFoundException) { return Results.NotFound(); }
        catch (StaleTextSelectionException)
        {
            return Results.Conflict(new { code = "text_selection_stale" });
        }
        catch (UnsupportedTextSelectionException)
        {
            return Results.UnprocessableEntity(new { code = "text_selection_unsupported" });
        }
        catch (InvalidTextSelectionException)
        {
            return Results.UnprocessableEntity(new { code = "text_selection_invalid" });
        }
        catch (TextEditValidationException exception)
        {
            return Results.UnprocessableEntity(new { code = exception.Code });
        }
    }

    private static async Task<IResult> ApplyAsync(Guid documentId, Guid pageId,
        ApplyTextEditRequest request, ICurrentUser user, IServiceProvider services,
        IOptions<TextEditingOptions> options, HttpContext context, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!options.Value.Enabled)
            return Results.Json(new { code = "text_edit_disabled" },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        if (request.WordIds is null ||
            request.ReplacementText is null || request.ReplacementBox is null ||
            request.Style is null || request.IdempotencyKey is null)
            return Results.UnprocessableEntity(new { code = "text_edit_invalid" });
        try
        {
            var command = services.GetRequiredService<CreateTextEdit>();
            var result = await command.HandleAsync(new CreateTextEditRequest(
                user.FirebaseUid, documentId, pageId, request.OcrResultId,
                request.ExpectedRevisionId, request.WordIds,
                request.ReplacementText, request.ReplacementBox, request.Style,
                request.IdempotencyKey), ct);
            return Results.Accepted($"/api/documents/{documentId}/pages/{pageId}/text-edits/{result.EditId}", result);
        }
        catch (TextSelectionNotFoundException)
        {
            return Results.NotFound();
        }
        catch (StaleTextSelectionException)
        {
            return Results.Conflict(new { code = "text_selection_stale" });
        }
        catch (TextEditConflictException)
        {
            return Results.Conflict(new { code = "text_edit_conflict" });
        }
        catch (UnsupportedTextSelectionException)
        {
            return Results.UnprocessableEntity(new { code = "text_selection_unsupported" });
        }
        catch (InvalidTextSelectionException)
        {
            return Results.UnprocessableEntity(new { code = "text_selection_invalid" });
        }
        catch (TextEditValidationException exception)
        {
            return Results.UnprocessableEntity(new { code = exception.Code });
        }
        catch (Exception exception) when (exception is KeyNotFoundException or ArgumentException)
        {
            return Results.UnprocessableEntity(new { code = "text_edit_invalid" });
        }
    }

    private static async Task<IResult> GetAsync(Guid documentId, Guid pageId,
        Guid editId, ICurrentUser user, GetTextEdit query,
        HttpContext context, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        try
        {
            return Results.Ok(await query.HandleAsync(user.FirebaseUid,
                documentId, pageId, editId, ct));
        }
        catch (TextSelectionNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> HistoryAsync(Guid documentId, Guid pageId,
        ICurrentUser user, GetPageEditHistory query,
        HttpContext context, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        try
        {
            return Results.Ok(await query.HandleAsync(user.FirebaseUid,
                documentId, pageId, ct));
        }
        catch (TextSelectionNotFoundException)
        {
            return Results.NotFound();
        }
    }
}
