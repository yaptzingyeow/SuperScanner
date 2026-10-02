using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ArksScanner.Api.Auth;
using ArksScanner.Domain.Documents;
using ArksScanner.Infrastructure.Persistence;
using ArksScanner.Infrastructure.Processing;

namespace ArksScanner.Api.Endpoints;

public static class CropEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public sealed record CropRequest(int Revision, CropPoint[]? Points, string? Filter = null, int? Rotation = null);

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/documents/{documentId:guid}/pages/{pageId:guid}/crop").RequireAuthorization();
        group.MapGet("/", async (Guid documentId, Guid pageId, ICurrentUser user, AppDbContext db, CancellationToken ct) =>
        {
            if (!await OwnsAsync(db, documentId, user.FirebaseUid, ct)) return Results.NotFound();
            var page = await db.Pages.AsNoTracking().SingleOrDefaultAsync(p => p.DocumentId == documentId && p.Id == pageId, ct);
            if (page?.CropSourceObjectKey is null) return Results.NotFound();
            return Results.Ok(ToDto(page));
        });
        group.MapPost("/apply", (Guid documentId, Guid pageId, CropRequest request, ICurrentUser user,
            AppDbContext db, CancellationToken ct) => SubmitAsync(documentId, pageId, request, false, user, db, ct));
        group.MapPost("/detect", (Guid documentId, Guid pageId, CropRequest request, ICurrentUser user,
            AppDbContext db, CancellationToken ct) => SubmitAsync(documentId, pageId, request, true, user, db, ct));
    }

    private static async Task<IResult> SubmitAsync(Guid documentId, Guid pageId, CropRequest request,
        bool detect, ICurrentUser user, AppDbContext db, CancellationToken ct)
    {
        if (!await OwnsAsync(db, documentId, user.FirebaseUid, ct)) return Results.NotFound();
        if (request.Filter is not null && !ScanFilter.IsValid(request.Filter))
            return Results.ValidationProblem(new Dictionary<string, string[]> {
                ["filter"] = ["Choose Magic, Original, Document, Bright, Grayscale, BlackAndWhite, RemoveShadows, CleanDocument, CleanDocumentGentle, CleanDocumentStrong, or ContentClean."]
            });
        if (request.Rotation is not null && request.Rotation is not (0 or 90 or 180 or 270))
            return Results.ValidationProblem(new Dictionary<string, string[]> {
                ["rotation"] = ["Choose 0, 90, 180, or 270 degrees."]
            });
        if (!detect && !CropGeometry.IsValid(request.Points))
            return Results.ValidationProblem(new Dictionary<string, string[]> {
                ["points"] = ["Choose four clockwise corners that form a convex shape covering at least 1% of the image."]
            });
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var page = await CropDocumentStatus.LockSubmissionPageAsync(db, documentId, pageId, ct);
        if (page?.CropSourceObjectKey is null) return Results.NotFound();
        if (request.Revision != page.CropRevision) return Results.Conflict(new { message = "This crop changed. Reload before editing." });
        if (page.ActiveRevisionId is not null)
            return Results.Conflict(new { message = "This page has text edits. Restore its unedited version before cropping." });
        page.BeginCrop(detect, detect ? null : JsonSerializer.Serialize(request.Points, Json));
        if (request.Filter is not null) page.SetFilter(request.Filter);
        if (request.Rotation is not null) page.SetRotation(request.Rotation.Value);
        CropProcessor.Queue(db, page, detect);
        await db.SaveChangesAsync(ct);
        await CropDocumentStatus.RefreshAsync(db, documentId, ct);
        await transaction.CommitAsync(ct);
        return Results.Accepted(value: ToDto(page));
    }

    private static Task<bool> OwnsAsync(AppDbContext db, Guid id, string uid, CancellationToken ct) =>
        db.Documents.AnyAsync(d => d.Id == id && d.OwnerFirebaseUid == uid, ct);

    public static string GetGuidance(string? source, double? confidence) =>
        source == "FullImage" || confidence == 0 ? "manual"
        : source == "Ai" && confidence >= .78 ? "accurate"
        : "verify";

    private static object ToDto(Page page) => new {
        revision = page.CropRevision, appliedRevision = page.AppliedCropRevision,
        status = page.CropStatus, confidence = page.CropConfidence, source = page.CropSource,
        modelVersion = page.CropModelVersion, diagnosticsCode = page.CropDiagnosticsCode,
        guidance = GetGuidance(page.CropSource, page.CropConfidence),
        filter = page.Filter, appliedFilter = page.AppliedFilter, rotation = page.Rotation,
        points = page.CropPointsJson is null ? null : JsonSerializer.Deserialize<CropPoint[]>(page.CropPointsJson, Json)
    };
}
