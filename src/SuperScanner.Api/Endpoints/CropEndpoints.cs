using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SuperScanner.Api.Auth;
using SuperScanner.Domain.Documents;
using SuperScanner.Infrastructure.Persistence;
using SuperScanner.Infrastructure.Processing;

namespace SuperScanner.Api.Endpoints;

public static class CropEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public sealed record CropRequest(int Revision, CropPoint[]? Points, string? Filter = null);

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
                ["filter"] = ["Choose Original, Document, Bright, Grayscale, or BlackAndWhite."]
            });
        if (!detect && !CropGeometry.IsValid(request.Points))
            return Results.ValidationProblem(new Dictionary<string, string[]> {
                ["points"] = ["Choose four clockwise corners that form a convex shape covering at least 1% of the image."]
            });
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var page = await db.Pages.FromSqlInterpolated($"SELECT * FROM pages WHERE \"Id\" = {pageId} AND \"DocumentId\" = {documentId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (page?.CropSourceObjectKey is null) return Results.NotFound();
        if (request.Revision != page.CropRevision) return Results.Conflict(new { message = "This crop changed. Reload before editing." });
        page.BeginCrop(detect, detect ? null : JsonSerializer.Serialize(request.Points, Json));
        if (request.Filter is not null) page.SetFilter(request.Filter);
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
        filter = page.Filter, appliedFilter = page.AppliedFilter,
        points = page.CropPointsJson is null ? null : JsonSerializer.Deserialize<CropPoint[]>(page.CropPointsJson, Json)
    };
}
