using Microsoft.Extensions.Options;
using SuperScanner.Api.Auth;
using SuperScanner.Application.TextEditing;
using SuperScanner.Infrastructure.TextEditing;

namespace SuperScanner.Api.Endpoints;

public static class TextEditingEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/documents/{documentId:guid}/pages/{pageId:guid}/text-edits")
            .RequireAuthorization();
        group.MapPost("/style-proposal", ProposeAsync);
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
}
