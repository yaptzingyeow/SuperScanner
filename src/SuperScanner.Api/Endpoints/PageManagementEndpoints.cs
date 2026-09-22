using SuperScanner.Api.Auth;
using SuperScanner.Application.Documents;

namespace SuperScanner.Api.Endpoints;

public static class PageManagementEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/documents/{documentId:guid}").RequireAuthorization();
        group.MapPut("/page-order", ReorderAsync);
        group.MapDelete("/pages/{pageId:guid}", RemoveAsync);
    }

    private static async Task<IResult> ReorderAsync(Guid documentId, ReorderPagesRequest request,
        ICurrentUser user, ReorderPages reorder, CancellationToken ct)
    {
        try
        {
            return Results.Ok(await reorder.HandleAsync(user.FirebaseUid, documentId, request, ct));
        }
        catch (PageManagementNotFoundException) { return Results.NotFound(); }
        catch (PageOrderConflictException conflict)
        {
            return Results.Conflict(new { pageOrderRevision = conflict.PageOrderRevision });
        }
        catch (ArgumentException validation)
        {
            var revisionError = validation.ParamName == "expectedPageOrderRevision";
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [revisionError ? "expectedPageOrderRevision" : "pageIds"] =
                    [revisionError ? "A non-negative expected page order revision is required."
                        : "Page order must contain every active page exactly once."]
            });
        }
    }

    private static async Task<IResult> RemoveAsync(Guid documentId, Guid pageId,
        ICurrentUser user, RemovePage remove, CancellationToken ct)
    {
        try
        {
            await remove.HandleAsync(user.FirebaseUid, documentId, pageId, ct);
            return Results.NoContent();
        }
        catch (PageManagementNotFoundException) { return Results.NotFound(); }
    }
}
