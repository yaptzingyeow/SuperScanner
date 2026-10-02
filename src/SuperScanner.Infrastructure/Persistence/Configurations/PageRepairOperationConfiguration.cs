using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SuperScanner.Domain.Documents;

namespace SuperScanner.Infrastructure.Persistence.Configurations;

public sealed class PageRepairOperationConfiguration : IEntityTypeConfiguration<PageRepairOperation>
{
    public void Configure(EntityTypeBuilder<PageRepairOperation> builder)
    {
        builder.ToTable("page_repair_operations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SourceObjectKey).HasMaxLength(1024).IsRequired();
        builder.Property(x => x.RectanglesJson).HasMaxLength(2048).IsRequired();
        builder.Property(x => x.StrokesJson).HasMaxLength(16384).IsRequired();
        builder.Property(x => x.Kind).HasMaxLength(16).IsRequired();
        builder.Property(x => x.CandidatesJson).HasMaxLength(2048);
        builder.Property(x => x.State).HasMaxLength(16).IsRequired();
        builder.Property(x => x.PreviewObjectKey).HasMaxLength(1024);
        builder.HasIndex(x => new { x.PageId, x.CreatedAt });
        builder.HasOne<Page>().WithMany().HasForeignKey(x => x.PageId).OnDelete(DeleteBehavior.Cascade);
    }
}
