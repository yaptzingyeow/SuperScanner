using SuperScanner.Api.Auth;
using SuperScanner.Application.Documents;
using Microsoft.EntityFrameworkCore;
using SuperScanner.Infrastructure.Persistence;
using SuperScanner.Domain.Processing;
using SuperScanner.Domain.Uploads;

namespace SuperScanner.Api.Endpoints;

public static class DocumentsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/documents").RequireAuthorization();

        group.MapPost("/", CreateAsync);
        group.MapGet("/", ListAsync);
    }

    private static async Task<IResult> CreateAsync(
        CreateDocumentRequest request,
        ICurrentUser currentUser,
        CreateDocument createDocument,
        CancellationToken cancellationToken)
    {
        var title = request.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["title"] = ["Title must contain between 1 and 200 characters."]
            });
        }

        var document = await createDocument.HandleAsync(
            currentUser.FirebaseUid,
            title,
            cancellationToken);
        return Results.Created($"/api/documents/{document.Id}", document);
    }

    private static async Task<IResult> ListAsync(
        ICurrentUser currentUser,
        ListDocuments listDocuments,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var documents = await listDocuments.HandleAsync(currentUser.FirebaseUid, cancellationToken);
        var uploads = await db.UploadIntents.AsNoTracking()
            .Where(x => x.OwnerFirebaseUid == currentUser.FirebaseUid)
            .Select(x => new { x.Id, x.DocumentId, x.State }).ToListAsync(cancellationToken);
        var ids = uploads.Select(x => x.Id.ToString()).ToList();
        var failed = await db.ProcessingJobs.AsNoTracking()
            .Where(x => x.Type == "ProcessDocument" && x.Status == ProcessingJobStatus.Failed && ids.Contains(x.Payload))
            .Select(x => x.Payload).ToListAsync(cancellationToken);
        var failedDocuments = uploads.Where(x => failed.Contains(x.Id.ToString())).Select(x => x.DocumentId).ToHashSet();
        var rejectedDocuments = uploads.GroupBy(x => x.DocumentId)
            .Where(x => x.All(u => u.State == UploadIntentState.Rejected)).Select(x => x.Key).ToHashSet();
        return Results.Ok(documents.Select(x => x with {
            Status = failedDocuments.Contains(x.Id) ? "Failed" : rejectedDocuments.Contains(x.Id) ? "Rejected" : x.Status
        }));
    }

    public sealed record CreateDocumentRequest(string? Title);
}
