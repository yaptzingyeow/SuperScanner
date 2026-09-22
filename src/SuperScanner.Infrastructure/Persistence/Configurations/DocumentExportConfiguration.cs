using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SuperScanner.Domain.Documents;

namespace SuperScanner.Infrastructure.Persistence.Configurations;

public sealed class DocumentExportConfiguration : IEntityTypeConfiguration<DocumentExport>
{
    public void Configure(EntityTypeBuilder<DocumentExport> builder)
    {
        builder.ToTable("document_exports");
        builder.HasKey(export => export.Id);
        builder.Property(export => export.OwnerFirebaseUid).HasMaxLength(128).IsRequired();
        builder.Property(export => export.State).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(export => export.DocumentRevision).IsRequired();
        builder.Property(export => export.SnapshotJson).HasColumnType("jsonb").IsRequired();
        builder.Property(export => export.ReadyPageCount).IsRequired();
        builder.Property(export => export.ExcludedPageCount).IsRequired();
        builder.Property(export => export.OutputObjectKey).HasMaxLength(1024);
        builder.Property(export => export.FailureCode).HasMaxLength(64);
        builder.Property(export => export.CreatedAt).IsRequired();
        builder.Property(export => export.ExpiresAt).IsRequired();
        builder.HasIndex(export => new { export.OwnerFirebaseUid, export.DocumentId, export.CreatedAt });
        builder
            .HasOne<Document>()
            .WithMany()
            .HasForeignKey(export => export.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
