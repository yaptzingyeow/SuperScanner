using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SuperScanner.Api.Auth;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Documents;
using SuperScanner.Infrastructure.Persistence;

namespace SuperScanner.Api.Endpoints;

public static class PageMarkEndpoints
{
    private const string Route = "/api/documents/{documentId:guid}/pages/{pageId:guid}/marks";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(Route).RequireAuthorization();
        group.MapGet("", List);
        group.MapPost("", Create);
        group.MapPut("/{markId:guid}", Update);
        group.MapDelete("/{markId:guid}", Delete);
    }

    private static async Task<Document?> Owned(AppDbContext db, ICurrentUser user, Guid documentId, Guid pageId, bool locked, CancellationToken ct)
    {
        var query = locked ? db.Documents.FromSqlInterpolated($"SELECT * FROM documents WHERE \"Id\" = {documentId} FOR UPDATE") : db.Documents;
        var document = await query.SingleOrDefaultAsync(x => x.Id == documentId && x.OwnerFirebaseUid == user.FirebaseUid, ct);
        if (document is null) return null;
        await db.Entry(document).Collection(x => x.Pages).LoadAsync(ct);
        return document.ActivePages.Any(x => x.Id == pageId) ? document : null;
    }

    private static object Dto(PageMark mark) => new
    {
        mark.Id, mark.PageId, kind = mark.Kind.ToString(), mark.Box,
        mark.Style.Color, mark.Style.StrokeWidth, mark.Revision, isDeleted = mark.DeletedAt is not null
    };
    private static void NoCache(HttpContext context)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    }
    private static Task<PageMark?> Find(AppDbContext db, Guid documentId, Guid pageId, Guid markId, CancellationToken ct) =>
        db.PageMarks.SingleOrDefaultAsync(x => x.Id == markId && x.DocumentId == documentId && x.PageId == pageId && x.DeletedAt == null, ct);
    private static bool Parse(Input? input, out PageMarkKind kind, out SignatureBox? box, out PageMarkStyle? style)
    {
        kind = default; box = null; style = null;
        if (input?.Box is null || !Enum.TryParse<PageMarkKind>(input.Kind, false, out kind) || !Enum.IsDefined(kind)) return false;
        try { box = new SignatureBox(input.Box.X, input.Box.Y, input.Box.Width, input.Box.Height); style = new PageMarkStyle(input.Color, input.StrokeWidth); }
        catch (ArgumentException) { return false; }
        return true;
    }

    private static async Task<IResult> List(Guid documentId, Guid pageId, ICurrentUser user, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        NoCache(context);
        if (await Owned(db, user, documentId, pageId, false, ct) is null) return Results.NotFound();
        var marks = await db.PageMarks.AsNoTracking().Where(x => x.DocumentId == documentId && x.PageId == pageId && x.DeletedAt == null)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(ct);
        return Results.Ok(marks.Select(Dto));
    }

    private static async Task<IResult> Create(Guid documentId, Guid pageId, Input input, ICurrentUser user, AppDbContext db,
        IAuditWriter audit, HttpContext context, CancellationToken ct)
    {
        NoCache(context);
        if (await Owned(db, user, documentId, pageId, false, ct) is null) return Results.NotFound();
        if (!Guid.TryParse(context.Request.Headers["Idempotency-Key"], out var requestId) || requestId == Guid.Empty)
            return Results.BadRequest(new { code = "mark_request_id_required" });
        if (!Parse(input, out var kind, out var box, out var style)) return Results.UnprocessableEntity(new { code = "mark_invalid" });
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var document = await Owned(db, user, documentId, pageId, true, ct);
        if (document is null) return Results.NotFound();
        var existing = await db.PageMarks.SingleOrDefaultAsync(x => x.DocumentId == documentId && x.ClientRequestId == requestId, ct);
        if (existing is not null)
        {
            if (existing.PageId != pageId || !existing.MatchesOriginalCreate(kind, box!, style!))
                return Results.Conflict(new { code = "mark_request_conflict" });
            return Results.Ok(Dto(existing));
        }
        var now = DateTimeOffset.UtcNow;
        var mark = PageMark.Create(Guid.NewGuid(), documentId, pageId, requestId, kind, box!, style!, now);
        db.PageMarks.Add(mark);
        document.MarkContentChanged(now);
        await AppendAudit(audit, user, documentId, mark.Id, "mark_created", now, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Created($"{context.Request.Path}/{mark.Id}", Dto(mark));
    }

    private static async Task<IResult> Update(Guid documentId, Guid pageId, Guid markId, UpdateInput input, ICurrentUser user,
        AppDbContext db, IAuditWriter audit, HttpContext context, CancellationToken ct)
    {
        NoCache(context);
        if (!Parse(input, out var kind, out var box, out var style)) return Results.UnprocessableEntity(new { code = "mark_invalid" });
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var document = await Owned(db, user, documentId, pageId, true, ct);
        if (document is null) return Results.NotFound();
        var mark = await Find(db, documentId, pageId, markId, ct);
        if (mark is null) return Results.NotFound();
        if (mark.Revision != input.ExpectedRevision) return Results.Conflict(new { code = "mark_revision_conflict" });
        var now = DateTimeOffset.UtcNow;
        mark.Update(kind, box!, style!, input.ExpectedRevision, now);
        document.MarkContentChanged(now);
        await AppendAudit(audit, user, documentId, markId, "mark_updated", now, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(Dto(mark));
    }

    private static async Task<IResult> Delete(Guid documentId, Guid pageId, Guid markId, long expectedRevision, ICurrentUser user,
        AppDbContext db, IAuditWriter audit, HttpContext context, CancellationToken ct)
    {
        NoCache(context);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var document = await Owned(db, user, documentId, pageId, true, ct);
        if (document is null) return Results.NotFound();
        var mark = await Find(db, documentId, pageId, markId, ct);
        if (mark is null) return Results.NotFound();
        if (mark.Revision != expectedRevision) return Results.Conflict(new { code = "mark_revision_conflict" });
        var now = DateTimeOffset.UtcNow;
        mark.Delete(expectedRevision, now);
        document.MarkContentChanged(now);
        await AppendAudit(audit, user, documentId, markId, "mark_deleted", now, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }

    private static Task<Guid> AppendAudit(IAuditWriter audit, ICurrentUser user, Guid documentId, Guid markId, string action, DateTimeOffset now, CancellationToken ct) =>
        audit.AppendAsync(new(user.FirebaseUid, action, "document", documentId, JsonSerializer.Serialize(new { markId }), now), ct);

    public sealed record BoxInput(double X, double Y, double Width, double Height);
    public record Input(string Kind, BoxInput Box, string Color, double StrokeWidth);
    public sealed record UpdateInput(string Kind, BoxInput Box, string Color, double StrokeWidth, long ExpectedRevision) : Input(Kind, Box, Color, StrokeWidth);
}
