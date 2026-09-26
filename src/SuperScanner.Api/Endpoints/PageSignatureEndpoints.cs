using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SuperScanner.Api.Auth;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Documents;
using SuperScanner.Infrastructure.Persistence;
using SuperScanner.Infrastructure.Signatures;

namespace SuperScanner.Api.Endpoints;

public static class PageSignatureEndpoints
{
    private const string Route = "/api/documents/{documentId:guid}/pages/{pageId:guid}/signatures";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(Route).RequireAuthorization();
        group.MapGet("", List);
        group.MapPost("", Create).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(6 * 1024 * 1024));
        group.MapGet("/{signatureId:guid}/image", Image);
        group.MapPut("/{signatureId:guid}", Update);
        group.MapDelete("/{signatureId:guid}", Delete);
    }

    private static async Task<Document?> Owned(AppDbContext db, ICurrentUser user, Guid documentId, Guid pageId, bool locked, CancellationToken ct)
    {
        var query = locked ? db.Documents.FromSqlInterpolated($"SELECT * FROM documents WHERE \"Id\" = {documentId} FOR UPDATE") : db.Documents;
        var document = await query.SingleOrDefaultAsync(x => x.Id == documentId && x.OwnerFirebaseUid == user.FirebaseUid, ct);
        if (document is null) return null;
        await db.Entry(document).Collection(x => x.Pages).LoadAsync(ct);
        return document.ActivePages.Any(x => x.Id == pageId) ? document : null;
    }
    private static object Dto(PageSignature signature) => new
    {
        signature.Id, signature.PageId, signature.Box, signature.ImageAspectRatio, signature.Revision,
        imageUrl = $"/api/documents/{signature.DocumentId}/pages/{signature.PageId}/signatures/{signature.Id}/image"
    };
    private static void NoCache(HttpContext context)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    }
    private static Task<PageSignature?> Find(AppDbContext db, Guid documentId, Guid pageId, Guid signatureId, CancellationToken ct) =>
        db.PageSignatures.SingleOrDefaultAsync(x => x.Id == signatureId && x.DocumentId == documentId && x.PageId == pageId && x.DeletedAt == null, ct);

    private static async Task<IResult> List(Guid documentId, Guid pageId, ICurrentUser user, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        NoCache(context);
        if (await Owned(db, user, documentId, pageId, false, ct) is null) return Results.NotFound();
        var signatures = await db.PageSignatures.AsNoTracking().Where(x => x.DocumentId == documentId && x.PageId == pageId && x.DeletedAt == null)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(ct);
        return Results.Ok(signatures.Select(Dto));
    }
    private static async Task<IResult> Image(Guid documentId, Guid pageId, Guid signatureId, ICurrentUser user, AppDbContext db, IObjectStore store, HttpContext context, CancellationToken ct)
    {
        NoCache(context);
        if (await Owned(db, user, documentId, pageId, false, ct) is null) return Results.NotFound();
        var signature = await Find(db, documentId, pageId, signatureId, ct);
        return signature is null ? Results.NotFound() : Results.Stream(await store.OpenReadAsync(signature.AssetKey, ct), "image/png");
    }
    private static async Task<IResult> Create(Guid documentId, Guid pageId, ICurrentUser user, AppDbContext db,
        IObjectStore store, SignatureImageNormalizer normalizer, IAuditWriter audit, HttpContext context, CancellationToken ct)
    {
        NoCache(context);
        if (await Owned(db, user, documentId, pageId, false, ct) is null) return Results.NotFound();
        if (!Guid.TryParse(context.Request.Headers["Idempotency-Key"], out var requestId) || requestId == Guid.Empty)
            return Results.BadRequest(new { code = "signature_request_id_required" });
        SignatureBox box;
        NormalizedSignatureImage image;
        try
        {
            if (!context.Request.HasFormContentType) return Results.UnprocessableEntity(new { code = "signature_png_required" });
            var form = await context.Request.ReadFormAsync(ct);
            var file = form.Files.GetFile("image");
            if (file is null || form.Files.Count != 1) return Results.UnprocessableEntity(new { code = "signature_png_required" });
            var values = JsonSerializer.Deserialize<BoxInput>(form["box"].ToString(), Json) ?? throw new JsonException();
            box = values.ToBox();
            await using var input = file.OpenReadStream();
            image = await normalizer.NormalizeAsync(input, ct);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or ArgumentException)
        { return Results.UnprocessableEntity(new { code = "signature_image_or_box_invalid" }); }

        db.ChangeTracker.Clear(); // Re-read after acquiring the document lock.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var document = await Owned(db, user, documentId, pageId, true, ct);
        if (document is null) return Results.NotFound();
        var existing = await db.PageSignatures.SingleOrDefaultAsync(x => x.DocumentId == documentId && x.ClientRequestId == requestId, ct);
        if (existing != null)
        {
            if (existing.PageId != pageId || existing.DeletedAt != null) return Results.Conflict(new { code = "signature_request_conflict" });
            await using var old = await store.OpenReadAsync(existing.AssetKey, ct);
            var oldHash = await SHA256.HashDataAsync(old, ct);
            if (!CryptographicOperations.FixedTimeEquals(oldHash, SHA256.HashData(image.Bytes)))
                return Results.Conflict(new { code = "signature_request_conflict" });
            return Results.Ok(Dto(existing));
        }
        var id = Guid.NewGuid();
        var key = $"documents/{documentId:N}/signatures/{id:N}.png";
        await using (var journalScope = context.RequestServices.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope())
        {
            var journal = journalScope.ServiceProvider.GetRequiredService<AppDbContext>();
            journal.SignatureAssetWriteIntents.Add(new SignatureAssetWriteIntent
                { Id = id, DocumentId = documentId, AssetKey = key, CreatedAt = DateTimeOffset.UtcNow });
            await journal.SaveChangesAsync(ct);
        }
        var written = false;
        try
        {
            written = await store.WriteIfAbsentAsync(key, "image/png", new MemoryStream(image.Bytes), ct) == ObjectCreationResult.Created;
            if (!written) return Results.Conflict(new { code = "signature_asset_conflict" });
            var now = DateTimeOffset.UtcNow;
            var signature = PageSignature.Create(id, documentId, pageId, requestId, key, image.AspectRatio, box, now);
            db.PageSignatures.Add(signature);
            var intent = await db.SignatureAssetWriteIntents.SingleAsync(x => x.Id == id, ct);
            db.SignatureAssetWriteIntents.Remove(intent);
            document.MarkContentChanged(now);
            await AppendAudit(audit, user, documentId, id, "signature_created", now, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Created($"{context.Request.Path}/{id}", Dto(signature));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
                if (written) await RecoverFailedWrite(context.RequestServices, store, key);
            }
            catch (Exception) { /* Durable journal retries cleanup; do not lose recovery on outage. */ }
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        catch (OperationCanceledException)
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
                if (written) await RecoverFailedWrite(context.RequestServices, store, key);
            }
            catch (Exception) { /* Durable journal retries cleanup. */ }
            throw;
        }
    }
    private static async Task RecoverFailedWrite(IServiceProvider services, IObjectStore store, string key)
    {
        await using var scope = services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        var verification = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // A commit acknowledgement can fail after the commit succeeded. Never erase its ink.
        if (!await verification.PageSignatures.AnyAsync(s => s.AssetKey == key))
            await store.DeleteAsync(key, CancellationToken.None);
    }
    private static async Task<IResult> Update(Guid documentId, Guid pageId, Guid signatureId, UpdateInput input, ICurrentUser user,
        AppDbContext db, IAuditWriter audit, HttpContext context, CancellationToken ct)
    {
        NoCache(context);
        if (input.Box is null) return Results.UnprocessableEntity(new { code = "signature_box_required" });
        SignatureBox box;
        try { box = input.Box.ToBox(); }
        catch (ArgumentException) { return Results.UnprocessableEntity(new { code = "signature_box_invalid" }); }
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var document = await Owned(db, user, documentId, pageId, true, ct);
        if (document is null) return Results.NotFound();
        var signature = await Find(db, documentId, pageId, signatureId, ct);
        if (signature is null) return Results.NotFound();
        if (signature.Revision != input.ExpectedRevision) return Results.Conflict(new { code = "signature_revision_conflict" });
        var now = DateTimeOffset.UtcNow;
        signature.MoveResize(box, input.ExpectedRevision, now);
        document.MarkContentChanged(now);
        await AppendAudit(audit, user, documentId, signatureId, "signature_updated", now, ct);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return Results.Ok(Dto(signature));
    }
    private static async Task<IResult> Delete(Guid documentId, Guid pageId, Guid signatureId, long expectedRevision, ICurrentUser user,
        AppDbContext db, IAuditWriter audit, HttpContext context, CancellationToken ct)
    {
        NoCache(context);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var document = await Owned(db, user, documentId, pageId, true, ct);
        if (document is null) return Results.NotFound();
        var signature = await Find(db, documentId, pageId, signatureId, ct);
        if (signature is null) return Results.NotFound();
        if (signature.Revision != expectedRevision) return Results.Conflict(new { code = "signature_revision_conflict" });
        var now = DateTimeOffset.UtcNow;
        signature.Delete(expectedRevision, now); document.MarkContentChanged(now);
        await AppendAudit(audit, user, documentId, signatureId, "signature_deleted", now, ct);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return Results.NoContent();
    }
    private static Task<Guid> AppendAudit(IAuditWriter audit, ICurrentUser user, Guid documentId, Guid signatureId, string action, DateTimeOffset now, CancellationToken ct) =>
        audit.AppendAsync(new(user.FirebaseUid, action, "document", documentId, JsonSerializer.Serialize(new { signatureId }), now), ct);
    public sealed record BoxInput(double X, double Y, double Width, double Height)
    {
        public SignatureBox ToBox() => new(X, Y, Width, Height);
    }
    public sealed record UpdateInput(BoxInput Box, long ExpectedRevision);
}
