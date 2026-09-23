using Microsoft.Extensions.Options;
using SuperScanner.Api.Auth;
using SuperScanner.Application.TextEditing;
using SuperScanner.Infrastructure.TextEditing;
using SuperScanner.Domain.TextEditing;

namespace SuperScanner.Api.Endpoints;

public static class TextEditingEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/documents/{documentId:guid}/pages/{pageId:guid}/text-edits")
            .RequireAuthorization();
        group.MapPost("/style-proposal", ProposeAsync);
        group.MapPost("", ApplyAsync);
        group.MapGet("/history", HistoryAsync);
        group.MapGet("/{editId:guid}", GetAsync);
    }

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

    public sealed record ApplyTextEditRequest(Guid OcrResultId,
        Guid? ExpectedRevisionId, Guid[]? WordIds, string? ReplacementText,
        NormalizedBox? ReplacementBox, TextEditStyle? Style, string? IdempotencyKey);

    private static async Task<IResult> ApplyAsync(Guid documentId, Guid pageId,
        ApplyTextEditRequest request, ICurrentUser user, IServiceProvider services,
        IOptions<TextEditingOptions> options, HttpContext context, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!options.Value.Enabled)
            return Results.Json(new { code = "text_edit_disabled" },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        if (request.OcrResultId == Guid.Empty || request.WordIds is null ||
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
        catch (Exception exception) when (exception is TextEditValidationException or
            KeyNotFoundException or ArgumentException)
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
