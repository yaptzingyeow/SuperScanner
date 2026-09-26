using Microsoft.EntityFrameworkCore;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.Uploads;
using SuperScanner.Domain.Processing;
using SuperScanner.Domain.Auditing;
using SuperScanner.Domain.Ocr;
using SuperScanner.Domain.TextEditing;

namespace SuperScanner.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Document> Documents => Set<Document>();

    public DbSet<Page> Pages => Set<Page>();

    public DbSet<DocumentExport> DocumentExports => Set<DocumentExport>();

    public DbSet<PageSignature> PageSignatures => Set<PageSignature>();

    public DbSet<UploadIntent> UploadIntents => Set<UploadIntent>();

    public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<PageOcrResult> PageOcrResults => Set<PageOcrResult>();

    public DbSet<OcrElement> OcrElements => Set<OcrElement>();

    public DbSet<PageRevision> PageRevisions => Set<PageRevision>();

    public DbSet<TextEditOperation> TextEditOperations => Set<TextEditOperation>();

    public DbSet<FontCatalogueEntry> FontCatalogueEntries => Set<FontCatalogueEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
