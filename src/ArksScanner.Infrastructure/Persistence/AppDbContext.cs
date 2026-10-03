using Microsoft.EntityFrameworkCore;
using ArksScanner.Domain.Documents;
using ArksScanner.Domain.Uploads;
using ArksScanner.Domain.Processing;
using ArksScanner.Domain.Auditing;
using ArksScanner.Domain.Ocr;
using ArksScanner.Domain.TextEditing;

namespace ArksScanner.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Document> Documents => Set<Document>();

    public DbSet<Page> Pages => Set<Page>();

    public DbSet<DocumentExport> DocumentExports => Set<DocumentExport>();

    public DbSet<PageSignature> PageSignatures => Set<PageSignature>();
    public DbSet<PageMark> PageMarks => Set<PageMark>();
    public DbSet<ArksScanner.Infrastructure.Signatures.SignatureAssetWriteIntent> SignatureAssetWriteIntents =>
        Set<ArksScanner.Infrastructure.Signatures.SignatureAssetWriteIntent>();

    public DbSet<UploadIntent> UploadIntents => Set<UploadIntent>();

    public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<PageOcrResult> PageOcrResults => Set<PageOcrResult>();

    public DbSet<OcrElement> OcrElements => Set<OcrElement>();

    public DbSet<PageRevision> PageRevisions => Set<PageRevision>();

    public DbSet<PageRepairOperation> PageRepairOperations => Set<PageRepairOperation>();

    public DbSet<TextEditOperation> TextEditOperations => Set<TextEditOperation>();

    public DbSet<FontCatalogueEntry> FontCatalogueEntries => Set<FontCatalogueEntry>();

    public DbSet<ArksScanner.Domain.Plans.Account> Accounts => Set<ArksScanner.Domain.Plans.Account>();
    public DbSet<ArksScanner.Domain.Plans.Subscription> Subscriptions => Set<ArksScanner.Domain.Plans.Subscription>();
    public DbSet<ArksScanner.Domain.Plans.AdminMember> Admins => Set<ArksScanner.Domain.Plans.AdminMember>();
    public DbSet<ArksScanner.Domain.Plans.PlanSettings> PlanSettings => Set<ArksScanner.Domain.Plans.PlanSettings>();
    public DbSet<ArksScanner.Domain.Plans.UsageDay> UsageDays => Set<ArksScanner.Domain.Plans.UsageDay>();
    public DbSet<ArksScanner.Domain.Plans.Payment> Payments => Set<ArksScanner.Domain.Plans.Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
