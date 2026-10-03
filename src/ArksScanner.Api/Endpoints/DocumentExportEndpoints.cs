using ArksScanner.Api.Auth;
using ArksScanner.Application.Documents;
using System.Text.Json;
using ArksScanner.Application.Abstractions;
using ArksScanner.Domain.Documents;

namespace ArksScanner.Api.Endpoints;

public static class DocumentExportEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/documents/{id:guid}/exports").RequireAuthorization();
        group.MapGet("/preview", async (Guid id, ICurrentUser user, GetDocumentExportPreview get,
            HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            try { return Results.Ok(await get.HandleAsync(user.FirebaseUid, id, ct)); }
            catch (DocumentExportNotFoundException) { return Results.NotFound(); }
        });
        group.MapPost("", async (Guid id, ICurrentUser user, CreateDocumentExport create, HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            CreateExportRequest? request;
            try
            {
                request = context.Request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true
                    ? await context.Request.ReadFromJsonAsync<CreateExportRequest>(cancellationToken: ct)
                    : null;
            }
            catch (Exception exception) when (exception is JsonException or BadHttpRequestException)
            { return Results.BadRequest(); }
            var layout = request?.PageLayout ?? "Original";
            if (layout is not ("Original" or "A4")) return Results.BadRequest();
            ExportWatermark? watermark = null;
            if (request?.Watermark is { } w)
            {
                try
                {
                    watermark = new ExportWatermark(w.Text ?? string.Empty, w.Layout ?? "Single", w.FontId ?? "noto-sans",
                        w.Bold ?? true, w.Color ?? "#C62828", w.Opacity ?? .25, w.SizePercent ?? 8,
                        w.AngleDegrees ?? 35, w.Spacing ?? 1.5, w.Position ?? "Center");
                }
                catch (ArgumentException)
                {
                    return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
                        title: "Invalid watermark.",
                        extensions: new Dictionary<string, object?> { ["code"] = "export_watermark_invalid" });
                }
            }
            try
            {
                var result = await create.HandleAsync(user.FirebaseUid, id, ct, layout,
                    request?.IncludeSearchableText ?? false, watermark);
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

    private sealed record CreateExportRequest(string? PageLayout, bool IncludeSearchableText = false,
        WatermarkRequest? Watermark = null);

    private sealed record WatermarkRequest(string? Text, string? Layout, string? FontId, bool? Bold, string? Color,
        double? Opacity, double? SizePercent, double? AngleDegrees, double? Spacing, string? Position);
}
