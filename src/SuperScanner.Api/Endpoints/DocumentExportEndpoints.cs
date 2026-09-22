using SuperScanner.Api.Auth;
using SuperScanner.Application.Documents;
using System.Text.Json;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Documents;

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
        group.MapGet("/{exportId:guid}/download", async (Guid id, Guid exportId, ICurrentUser user,
            IDocumentRepository documents, IDocumentExportRepository exports, IObjectStore store,
            IAuditWriter audit, IClock clock, HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            await using var transaction = await documents.BeginTransactionAsync(ct);
            var document = await documents.FindOwnedForUpdateAsync(user.FirebaseUid, id, ct);
            if (document is null) return Results.NotFound();
            var owned = await exports.FindOwnedAsync(user.FirebaseUid, id, exportId, ct);
            if (owned?.Export is not { State: DocumentExportState.Ready, OutputObjectKey: not null } export ||
                export.ExpiresAt <= clock.UtcNow) return Results.NotFound();
            var stream = await store.OpenReadAsync(export.OutputObjectKey, ct);
            try
            {
                await audit.AppendAsync(new AuditWriteRequest(user.FirebaseUid, "document.export_downloaded",
                    "document", id, JsonSerializer.Serialize(new { exportId }), clock.UtcNow), ct);
                await exports.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return Results.Stream(stream, "application/pdf", fileDownloadName: SafeFilename(document.Title));
            }
            catch
            {
                await stream.DisposeAsync();
                throw;
            }
        });
    }

    private static string SafeFilename(string title)
    {
        var name = new string(title.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').Take(120).ToArray()).Trim();
        return (name.Length == 0 ? "document" : name) + ".pdf";
    }
}
