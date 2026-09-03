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
        builder.Property(page => page.OriginalObjectKey).HasMaxLength(1024);
        builder.Property(page => page.CreatedAt).IsRequired();
        builder.HasIndex(page => new { page.DocumentId, page.PageNumber }).IsUnique();
    }
}
