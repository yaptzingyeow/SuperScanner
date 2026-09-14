using Microsoft.EntityFrameworkCore;
using SuperScanner.Domain.Documents;
using SuperScanner.Infrastructure.Persistence;

namespace SuperScanner.Infrastructure.Processing;

public static class CropDocumentStatus
{
    public static Task RefreshAsync(AppDbContext db, Guid documentId, CancellationToken ct) =>
        db.Documents.Where(d => d.Id == documentId).ExecuteUpdateAsync(set => set
            .SetProperty(d => d.Status, d =>
                d.Pages.Any(p => p.CropStatus == "Detecting" || p.CropStatus == "Processing") ? DocumentStatus.Processing :
                d.Pages.Any(p => p.CropStatus == "Failed") ? DocumentStatus.Failed :
                d.Pages.Any(p => p.CropStatus == "NeedsCrop") ? DocumentStatus.NeedsCrop :
                d.Pages.All(p => p.PreviewObjectKey != null) ? DocumentStatus.Ready : DocumentStatus.Uploading)
            .SetProperty(d => d.UpdatedAt, DateTimeOffset.UtcNow), ct);
}
