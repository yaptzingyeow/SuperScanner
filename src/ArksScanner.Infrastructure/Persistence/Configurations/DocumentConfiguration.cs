using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ArksScanner.Domain.Documents;

namespace ArksScanner.Infrastructure.Persistence.Configurations;

public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents");
        builder.HasKey(document => document.Id);
        builder.Property(document => document.OwnerFirebaseUid).HasMaxLength(128).IsRequired();
        builder.Property(document => document.Title).HasMaxLength(200).IsRequired();
        builder.Property(document => document.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(document => document.RemovedReason).HasMaxLength(32);
        builder.Property(document => document.Revision).IsConcurrencyToken().IsRequired();
        builder.Property(document => document.PageOrderRevision).IsConcurrencyToken().IsRequired();
        builder.Property(document => document.CreatedAt).IsRequired();
        builder.Property(document => document.UpdatedAt).IsRequired();
        builder.HasIndex(document => new { document.OwnerFirebaseUid, document.UpdatedAt });
        builder
            .HasMany(document => document.Pages)
            .WithOne()
            .HasForeignKey(page => page.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(document => document.Pages).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(document => document.ActivePages);
        // Removed documents (Free-plan retention) vanish from every query; jobs that need them use IgnoreQueryFilters.
        builder.HasQueryFilter(document => document.RemovedAt == null);
    }
}
