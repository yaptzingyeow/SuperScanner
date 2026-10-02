using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SuperScanner.Api.Auth;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.Processing;
using SuperScanner.Domain.TextEditing;
using SuperScanner.Infrastructure.Persistence;

namespace SuperScanner.Api.Endpoints;

public static class PageRepairEndpoints
{
    public sealed record RepairStroke(double Radius, double[][] Points);
    public sealed record RepairRequest(Guid? SourceRevisionId, double[][] Rectangles,
        RepairStroke[]? Strokes = null, int? SourceCropRevision = null);

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/documents/{documentId:guid}/pages/{pageId:guid}/repair")
            .RequireAuthorization();
        group.MapPost("/previews", CreatePreviewAsync);
        group.MapPost("/suggestions", CreateSuggestionsAsync);
        group.MapGet("/previews/{operationId:guid}", GetPreviewAsync);
        group.MapGet("/previews/{operationId:guid}/image", GetPreviewImageAsync);
        group.MapPost("/previews/{operationId:guid}/apply", ApplyAsync);
    }

    private static bool Valid(RepairRequest request) =>
        request.Rectangles is { Length: <= 20 } &&
        request.Strokes is null or { Length: <= 20 } &&
        request.Rectangles.Length + (request.Strokes?.Length ?? 0) is >= 1 and <= 20 &&
        request.Rectangles.All(box => box is { Length: 4 } && box.All(double.IsFinite) &&
            box[0] >= 0 && box[1] >= 0 && box[2] <= 1 && box[3] <= 1 &&
            box[0] < box[2] && box[1] < box[3] &&
            (box[2] - box[0]) * (box[3] - box[1]) <= .03) &&
        request.Rectangles.Sum(box => (box[2] - box[0]) * (box[3] - box[1])) <= .05 &&
        (request.Strokes ?? []).All(stroke => double.IsFinite(stroke.Radius) &&
            stroke.Radius is > 0 and <= .05 && stroke.Points is { Length: >= 1 and <= 256 } &&
            stroke.Points.All(point => point is { Length: 2 } &&
                point.All(value => double.IsFinite(value) && value is >= 0 and <= 1))) &&
        JsonSerializer.Serialize((request.Strokes ?? []).Select(stroke =>
            new { radius = stroke.Radius, points = stroke.Points })).Length <= 16384;

    private static async Task<IResult> CreatePreviewAsync(Guid documentId, Guid pageId,
        RepairRequest request, ICurrentUser user, AppDbContext db, CancellationToken ct)
    {
        if (!Valid(request)) return Results.BadRequest(new { message = "Choose small repair areas." });
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var page = await db.Pages.FromSqlInterpolated($"""
            SELECT p.* FROM pages AS p JOIN documents AS d ON d."Id" = p."DocumentId"
            WHERE p."Id" = {pageId} AND p."DocumentId" = {documentId}
              AND d."OwnerFirebaseUid" = {user.FirebaseUid} AND p."RemovedAt" IS NULL
            FOR UPDATE OF p
            """).Include(x => x.ActiveRevision).SingleOrDefaultAsync(ct);
        if (page is null) return Results.NotFound();
        if (page.State != PageState.Ready || page.ActiveRevisionId != request.SourceRevisionId ||
            (page.ActiveRevisionId is null && request.SourceCropRevision != page.AppliedCropRevision))
            return Results.Conflict(new { message = "Page changed. Reload before cleaning." });
        var operation = PageRepairOperation.Create(Guid.NewGuid(), pageId, page.ActiveRevisionId,
            page.GetProcessedObjectKey(), JsonSerializer.Serialize(request.Rectangles), DateTimeOffset.UtcNow,
            JsonSerializer.Serialize((request.Strokes ?? []).Select(stroke =>
                new { radius = stroke.Radius, points = stroke.Points })));
        db.PageRepairOperations.Add(operation);
        db.ProcessingJobs.Add(ProcessingJob.Create(Guid.NewGuid(), "PreviewPageRepair",
            operation.Id.ToString(), $"page:{pageId}:repair:{operation.Id}", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Accepted(value: new { operationId = operation.Id, state = operation.State });
    }

    private static async Task<IResult> CreateSuggestionsAsync(Guid documentId, Guid pageId,
        ICurrentUser user, AppDbContext db, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var page = await db.Pages.FromSqlInterpolated($"""
            SELECT p.* FROM pages AS p JOIN documents AS d ON d."Id" = p."DocumentId"
            WHERE p."Id" = {pageId} AND p."DocumentId" = {documentId}
              AND d."OwnerFirebaseUid" = {user.FirebaseUid} AND p."RemovedAt" IS NULL
            FOR UPDATE OF p
            """).Include(x => x.ActiveRevision).SingleOrDefaultAsync(ct);
        if (page is null) return Results.NotFound();
        if (page.State != PageState.Ready) return Results.Conflict();
        var operation = PageRepairOperation.CreateSuggestion(Guid.NewGuid(), pageId,
            page.ActiveRevisionId, page.GetProcessedObjectKey(), DateTimeOffset.UtcNow);
        db.PageRepairOperations.Add(operation);
        db.ProcessingJobs.Add(ProcessingJob.Create(Guid.NewGuid(), "PreviewPageRepair",
            operation.Id.ToString(), $"page:{pageId}:repair:{operation.Id}", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Accepted(value: new { operationId = operation.Id, state = operation.State });
    }

    private static async Task<IResult> GetPreviewAsync(Guid documentId, Guid pageId,
        Guid operationId, ICurrentUser user, AppDbContext db, CancellationToken ct)
    {
        var operation = await OwnedOperation(db, documentId, pageId, operationId, user.FirebaseUid, ct);
        return operation is null ? Results.NotFound() : Results.Ok(new {
            operationId, state = operation.State, sourceRevisionId = operation.SourceRevisionId,
            hasPreview = operation.PreviewObjectKey is not null,
            candidates = operation.CandidatesJson is null ? null :
                JsonSerializer.Deserialize<double[][]>(operation.CandidatesJson)
        });
    }

    private static async Task<IResult> GetPreviewImageAsync(Guid documentId, Guid pageId,
        Guid operationId, ICurrentUser user, AppDbContext db, IObjectStore store,
        HttpContext context, CancellationToken ct)
    {
        var operation = await OwnedOperation(db, documentId, pageId, operationId, user.FirebaseUid, ct);
        if (operation?.State != "Ready" || operation.PreviewObjectKey is null) return Results.NotFound();
        context.Response.Headers.CacheControl = "private, no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Results.Stream(await store.OpenReadAsync(operation.PreviewObjectKey, ct), "image/png");
    }

    private static async Task<IResult> ApplyAsync(Guid documentId, Guid pageId,
        Guid operationId, ICurrentUser user, AppDbContext db, IObjectStore store,
        IAuditWriter audit, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var document = await db.Documents.FromSqlInterpolated($"""
            SELECT * FROM documents WHERE "Id" = {documentId}
              AND "OwnerFirebaseUid" = {user.FirebaseUid} FOR UPDATE
            """).SingleOrDefaultAsync(ct);
        if (document is null) return Results.NotFound();
        var page = await db.Pages.FromSqlInterpolated($"""
            SELECT p.* FROM pages AS p JOIN documents AS d ON d."Id" = p."DocumentId"
            WHERE p."Id" = {pageId} AND p."DocumentId" = {documentId}
              AND d."OwnerFirebaseUid" = {user.FirebaseUid} AND p."RemovedAt" IS NULL
            FOR UPDATE OF p
            """).Include(x => x.ActiveRevision).SingleOrDefaultAsync(ct);
        if (page is null) return Results.NotFound();
        var operation = await db.PageRepairOperations.SingleOrDefaultAsync(x => x.Id == operationId && x.PageId == pageId, ct);
        if (operation?.State != "Ready" || operation.PreviewObjectKey is null)
            return Results.Conflict(new { message = "Preview is not ready." });
        if (page.State != PageState.Ready || page.ActiveRevisionId != operation.SourceRevisionId ||
            page.GetProcessedObjectKey() != operation.SourceObjectKey)
            return Results.Conflict(new { message = "Page changed. Create a new preview." });
        await using var preview = await store.OpenReadAsync(operation.PreviewObjectKey, ct);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920]; long size = 0; int count;
        while ((count = await preview.ReadAsync(buffer, ct)) > 0)
        {
            size += count;
            if (size > 20 * 1024 * 1024) return Results.BadRequest();
            hash.AppendData(buffer, 0, count);
        }
        if (size == 0) return Results.BadRequest();
        var parentId = page.ActiveRevisionId;
        if (parentId is null)
        {
            var priorBase = await db.PageRevisions.FirstOrDefaultAsync(x => x.PageId == pageId &&
                x.ObjectKey == operation.SourceObjectKey, ct);
            if (priorBase is null)
            {
                await using var source = await store.OpenReadAsync(operation.SourceObjectKey, ct);
                var sourceHash = await SHA256.HashDataAsync(source, ct);
                priorBase = PageRevision.CreateBase(Guid.NewGuid(), pageId, operation.SourceObjectKey,
                    Convert.ToHexStringLower(sourceHash), DateTimeOffset.UtcNow);
                db.PageRevisions.Add(priorBase);
            }
            parentId = priorBase.Id;
        }
        var revision = PageRevision.CreateRepair(Guid.NewGuid(), pageId, parentId,
            operation.PreviewObjectKey, Convert.ToHexStringLower(hash.GetHashAndReset()), DateTimeOffset.UtcNow);
        db.PageRevisions.Add(revision);
        page.ActivateRevision(revision);
        operation.Apply(revision.Id);
        var now = DateTimeOffset.UtcNow;
        document.MarkContentChanged(now);
        await audit.AppendAsync(new AuditWriteRequest(user.FirebaseUid, "page.repair_applied",
            "document", documentId,
            JsonSerializer.Serialize(new { pageId, operationId, revisionId = revision.Id }), now), ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(new { revisionId = revision.Id });
    }

    private static Task<PageRepairOperation?> OwnedOperation(AppDbContext db, Guid documentId,
        Guid pageId, Guid operationId, string uid, CancellationToken ct) =>
        db.PageRepairOperations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == operationId &&
            x.PageId == pageId && db.Pages.Any(p => p.Id == pageId && p.DocumentId == documentId &&
                p.RemovedAt == null) && db.Documents.Any(d => d.Id == documentId &&
                d.OwnerFirebaseUid == uid), ct);
}
