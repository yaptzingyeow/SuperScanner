using SuperScanner.Api.Auth;
using SuperScanner.Application.Documents;

namespace SuperScanner.Api.Endpoints;

public static class DocumentExportEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/documents/{id:guid}/exports").RequireAuthorization();
        group.MapPost("", async (Guid id, ICurrentUser user, CreateDocumentExport create, HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            try
            {
                var result = await create.HandleAsync(user.FirebaseUid, id, ct);
                return Results.Accepted(result.StatusUrl, result);
            }
            catch (DocumentExportNotFoundException) { return Results.NotFound(); }
            catch (DocumentExportNoReadyPagesException)
            {
                return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
                    title: "No ready pages to export.",
                    extensions: new Dictionary<string, object?> { ["code"] = "export_no_ready_pages" });
            }
        });
        group.MapGet("/{exportId:guid}", async (Guid id, Guid exportId, ICurrentUser user,
            GetDocumentExport get, HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            try { return Results.Ok(await get.HandleAsync(user.FirebaseUid, id, exportId, ct)); }
            catch (DocumentExportNotFoundException) { return Results.NotFound(); }
        });
    }
}
