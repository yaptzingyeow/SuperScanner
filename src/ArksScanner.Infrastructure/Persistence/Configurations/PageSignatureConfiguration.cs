using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ArksScanner.Domain.Documents;

namespace ArksScanner.Infrastructure.Persistence.Configurations;

public sealed class PageSignatureConfiguration : IEntityTypeConfiguration<PageSignature>
{
    public void Configure(EntityTypeBuilder<PageSignature> builder)
    {
        builder.ToTable("page_signatures");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AssetKey).HasMaxLength(1024).IsRequired();
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.OwnsOne(x => x.Box, box =>
        {
            box.Property(x => x.X).HasColumnName("X");
            box.Property(x => x.Y).HasColumnName("Y");
            box.Property(x => x.Width).HasColumnName("Width");
            box.Property(x => x.Height).HasColumnName("Height");
        });
        builder.Navigation(x => x.Box).IsRequired();
        builder.HasIndex(x => new { x.DocumentId, x.PageId, x.DeletedAt });
        builder.HasIndex(x => new { x.DocumentId, x.ClientRequestId }).IsUnique();
        builder.HasOne<Document>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Page>().WithMany().HasForeignKey(x => x.PageId).OnDelete(DeleteBehavior.Cascade);
    }
}
