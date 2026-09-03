using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SuperScanner.Domain.Documents;

namespace SuperScanner.Infrastructure.Persistence.Configurations;

public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents");
        builder.HasKey(document => document.Id);
        builder.Property(document => document.OwnerFirebaseUid).HasMaxLength(128).IsRequired();
        builder.Property(document => document.Title).HasMaxLength(200).IsRequired();
        builder.Property(document => document.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(document => document.CreatedAt).IsRequired();
        builder.Property(document => document.UpdatedAt).IsRequired();
        builder.HasIndex(document => new { document.OwnerFirebaseUid, document.UpdatedAt });
        builder
            .HasMany(document => document.Pages)
            .WithOne()
            .HasForeignKey(page => page.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(document => document.Pages).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
