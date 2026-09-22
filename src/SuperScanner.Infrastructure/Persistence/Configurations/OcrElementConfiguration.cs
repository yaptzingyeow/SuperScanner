using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SuperScanner.Domain.Ocr;

namespace SuperScanner.Infrastructure.Persistence.Configurations;

public sealed class OcrElementConfiguration : IEntityTypeConfiguration<OcrElement>
{
    public void Configure(EntityTypeBuilder<OcrElement> builder)
    {
        builder.ToTable("ocr_elements");
        builder.HasKey(element => element.Id);
        builder.Property(element => element.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(element => element.Text).HasColumnType("text").IsRequired();
        builder.Property(element => element.Confidence).IsRequired();
        builder.Property(element => element.TextType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(element => element.ReadingOrder).IsRequired();
        builder.Property(element => element.PolygonJson).HasColumnType("jsonb").IsRequired();
        builder.Ignore(element => element.Polygon);
        builder.HasIndex(element => new
        {
            element.PageOcrResultId,
            element.ParentElementId,
            element.ReadingOrder
        });
        builder.HasOne<OcrElement>()
            .WithMany()
            .HasForeignKey(element => element.ParentElementId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
