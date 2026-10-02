using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ArksScanner.Domain.Documents;
using ArksScanner.Domain.TextEditing;

namespace ArksScanner.Infrastructure.Persistence.Configurations;

public sealed class PageRevisionConfiguration : IEntityTypeConfiguration<PageRevision>
{
    public void Configure(EntityTypeBuilder<PageRevision> builder)
    {
        builder.ToTable("page_revisions");
        builder.HasKey(revision => revision.Id);
        builder.Property(revision => revision.ObjectKey).HasMaxLength(1024).IsRequired();
        builder.Property(revision => revision.MediaType).HasMaxLength(128).IsRequired();
        builder.Property(revision => revision.Sha256Hex).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(revision => revision.CreatedAt).IsRequired();
        builder.HasIndex(revision => revision.ObjectKey).IsUnique();
        builder.HasIndex(revision => new { revision.PageId, revision.CreatedAt });
        builder.HasIndex(revision => revision.ParentRevisionId);
        builder.HasOne<Page>()
            .WithMany()
            .HasForeignKey(revision => revision.PageId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<PageRevision>()
            .WithMany()
            .HasForeignKey(revision => revision.ParentRevisionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TextEditOperation>()
            .WithOne()
            .HasForeignKey<PageRevision>(revision => revision.ProducingTextEditId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
