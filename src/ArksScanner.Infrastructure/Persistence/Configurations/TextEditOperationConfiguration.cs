using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ArksScanner.Domain.Documents;
using ArksScanner.Domain.TextEditing;

namespace ArksScanner.Infrastructure.Persistence.Configurations;

public sealed class TextEditOperationConfiguration : IEntityTypeConfiguration<TextEditOperation>
{
    public void Configure(EntityTypeBuilder<TextEditOperation> builder)
    {
        builder.ToTable("text_edit_operations");
        builder.HasKey(edit => edit.Id);
        builder.Property(edit => edit.ActorFirebaseUid).HasMaxLength(128).IsRequired();
        builder.Property(edit => edit.SelectedOcrElementIdsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(edit => edit.OriginalText).HasColumnType("text").IsRequired();
        builder.Property(edit => edit.ReplacementText).HasColumnType("text").IsRequired();
        builder.Property(edit => edit.ReplacementBoxJson).HasColumnType("jsonb").IsRequired();
        builder.Property(edit => edit.StyleJson).HasColumnType("jsonb").IsRequired();
        builder.Property(edit => edit.StyleProvenanceJson).HasColumnType("jsonb").IsRequired();
        builder.Property(edit => edit.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(edit => edit.CanonicalRequestHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(edit => edit.RendererVersion).HasMaxLength(64).IsRequired();
        builder.Property(edit => edit.LayoutVersion).HasMaxLength(64).IsRequired();
        builder.Property(edit => edit.State).HasConversion<string>().HasMaxLength(32).IsConcurrencyToken().IsRequired();
        builder.Property(edit => edit.FailureCode).HasMaxLength(64);
        builder.Property(edit => edit.QueuedAt).IsRequired();
        builder.Ignore(edit => edit.SelectedOcrElementIds);
        builder.Ignore(edit => edit.ReplacementBox);
        builder.Ignore(edit => edit.Style);

        builder.HasIndex(edit => new { edit.PageId, edit.IdempotencyKey }).IsUnique();
        builder.HasIndex(edit => new { edit.PageId, edit.Sequence });
        builder.HasIndex(edit => edit.ResultRevisionId)
            .IsUnique()
            .HasFilter("\"ResultRevisionId\" IS NOT NULL");

        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(edit => edit.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Page>()
            .WithMany()
            .HasForeignKey(edit => edit.PageId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PageRevision>()
            .WithMany()
            .HasForeignKey(edit => edit.SourceRevisionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TextEditOperation>()
            .WithMany()
            .HasForeignKey(edit => edit.BranchParentEditId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PageRevision>()
            .WithOne()
            .HasForeignKey<TextEditOperation>(edit => edit.ResultRevisionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
