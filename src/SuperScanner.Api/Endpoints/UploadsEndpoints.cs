using SuperScanner.Api.Auth;
using SuperScanner.Application.Uploads;

namespace SuperScanner.Api.Endpoints;

public static class UploadsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapPost("/api/documents/{documentId:guid}/uploads", CreateAsync)
            .RequireAuthorization();
        endpoints
            .MapPost(
                "/api/documents/{documentId:guid}/uploads/{uploadId:guid}/complete",
                CompleteAsync)
            .RequireAuthorization();
    }

    private static async Task<IResult> CreateAsync(
        Guid documentId,
        CreateUploadRequest request,
        ICurrentUser currentUser,
        CreateUploadIntent createUploadIntent,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await createUploadIntent.HandleAsync(
                currentUser.FirebaseUid,
                documentId,
                request,
                cancellationToken);
            return Results.Created(
                $"/api/documents/{documentId}/uploads/{result.UploadId}",
                result);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["upload"] = [exception.Message]
            });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
    }

    private static async Task<IResult> CompleteAsync(
        Guid documentId,
        Guid uploadId,
        ICurrentUser currentUser,
        CompleteUpload completeUpload,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await completeUpload.HandleAsync(
                currentUser.FirebaseUid,
                documentId,
                uploadId,
                cancellationToken));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidDataException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
    }
}
