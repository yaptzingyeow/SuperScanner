using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SuperScanner.Domain.Documents;

namespace SuperScanner.Infrastructure.Persistence.Configurations;

public sealed class PageConfiguration : IEntityTypeConfiguration<Page>
{
    public void Configure(EntityTypeBuilder<Page> builder)
    {
        builder.ToTable("pages");
        builder.HasKey(page => page.Id);
        builder.Property(page => page.PageNumber).IsRequired();
        builder.Property(page => page.Position).IsRequired();
        builder.Property(page => page.SourceUploadId).IsRequired();
        builder.Property(page => page.SourcePageIndex).IsRequired();
        builder.Property(page => page.State).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(page => page.FailureCode).HasMaxLength(64);
        builder.Property(page => page.OriginalObjectKey).HasMaxLength(1024);
        builder.Property(page => page.OriginalMediaType).HasMaxLength(128).IsRequired();
        builder.Property(page => page.CropModelVersion).HasMaxLength(100);
        builder.Property(page => page.CropDiagnosticsCode).HasMaxLength(64);
        builder.Property(page => page.CropRevision).IsConcurrencyToken();
        builder.Property(page => page.CreatedAt).IsRequired();
        builder.Property(page => page.RemovedByFirebaseUid).HasMaxLength(128);
        builder.HasIndex(page => new { page.DocumentId, page.Position })
            .IsUnique()
            .HasFilter("\"RemovedAt\" IS NULL");
        builder.HasIndex(page => new { page.SourceUploadId, page.SourcePageIndex }).IsUnique();
    }
}
