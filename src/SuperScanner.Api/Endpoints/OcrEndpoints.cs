using Microsoft.Extensions.Options;
using SuperScanner.Api.Auth;
using SuperScanner.Application.Ocr;
using SuperScanner.Infrastructure.Ocr;

namespace SuperScanner.Api.Endpoints;

public static class OcrEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/documents/{documentId:guid}/pages/{pageId:guid}/ocr")
            .RequireAuthorization();
        group.MapGet("", GetAsync);
        group.MapPost("", RequestAsync);
    }

    private static async Task<IResult> GetAsync(
        Guid documentId,
        Guid pageId,
        ICurrentUser user,
        GetPageOcr query,
        CancellationToken ct)
    {
        try
        {
            return Results.Ok(await query.HandleAsync(user.FirebaseUid, documentId, pageId, ct));
        }
        catch (OcrResourceNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> RequestAsync(
        Guid documentId,
        Guid pageId,
        RequestPageOcrRequest request,
        ICurrentUser user,
        RequestPageOcr command,
        IOptions<OcrOptions> options,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
            return Results.Json(new { code = "ocr_disabled" }, statusCode: StatusCodes.Status503ServiceUnavailable);

        try
        {
            var result = await command.HandleAsync(
                user.FirebaseUid, documentId, pageId, request.RetryFailed, ct);
            return Results.Accepted(value: result);
        }
        catch (OcrResourceNotFoundException)
        {
            return Results.NotFound();
        }
        catch (OcrPageNotReadyException)
        {
            return Results.Conflict(new { code = "page_not_ready" });
        }
        catch (OcrRetryNotAllowedException)
        {
            return Results.Conflict(new { code = "ocr_retry_not_allowed" });
        }
    }

    private sealed record RequestPageOcrRequest(bool RetryFailed = false);
}
