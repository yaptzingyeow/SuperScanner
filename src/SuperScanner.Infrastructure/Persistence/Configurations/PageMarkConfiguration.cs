using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SuperScanner.Domain.Documents;

namespace SuperScanner.Infrastructure.Persistence.Configurations;

public sealed class PageMarkConfiguration : IEntityTypeConfiguration<PageMark>
{
    public void Configure(EntityTypeBuilder<PageMark> builder)
    {
        builder.ToTable("page_marks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.OwnsOne(x => x.Box, box =>
        {
            box.Property(x => x.X).HasColumnName("X");
            box.Property(x => x.Y).HasColumnName("Y");
            box.Property(x => x.Width).HasColumnName("Width");
            box.Property(x => x.Height).HasColumnName("Height");
        });
        builder.Navigation(x => x.Box).IsRequired();
        builder.OwnsOne(x => x.Style, style =>
        {
            style.Property(x => x.Color).HasColumnName("Color").HasMaxLength(7).IsRequired();
            style.Property(x => x.StrokeWidth).HasColumnName("StrokeWidth");
        });
        builder.Navigation(x => x.Style).IsRequired();
        builder.HasIndex(x => new { x.DocumentId, x.PageId, x.DeletedAt });
        builder.HasIndex(x => new { x.DocumentId, x.ClientRequestId }).IsUnique();
        builder.HasOne<Document>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Page>().WithMany().HasForeignKey(x => x.PageId).OnDelete(DeleteBehavior.Cascade);
    }
}
