using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.Ocr;

namespace SuperScanner.Infrastructure.Persistence.Configurations;

public sealed class PageOcrResultConfiguration : IEntityTypeConfiguration<PageOcrResult>
{
    public void Configure(EntityTypeBuilder<PageOcrResult> builder)
    {
        builder.ToTable("page_ocr_results");
        builder.HasKey(result => result.Id);
        builder.Property(result => result.SourceObjectKey).HasMaxLength(1024).IsRequired();
        builder.Property(result => result.SourceFingerprint).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(result => result.State).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(result => result.Language).HasMaxLength(8).IsRequired();
        builder.Property(result => result.FullText).HasColumnType("text").IsRequired();
        builder.Property(result => result.ProviderName).HasMaxLength(128);
        builder.Property(result => result.ProviderModelVersion).HasMaxLength(128);
        builder.Property(result => result.FailureCode).HasMaxLength(64);
        builder.Property(result => result.QueuedAt).IsRequired();
        builder.HasIndex(result => new { result.PageId, result.SourceFingerprint }).IsUnique();
        builder.HasIndex(result => new { result.PageId, result.State, result.QueuedAt });
        builder.HasOne<Page>()
            .WithMany()
            .HasForeignKey(result => result.PageId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(result => result.Elements)
            .WithOne()
            .HasForeignKey(element => element.PageOcrResultId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(result => result.Elements).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
