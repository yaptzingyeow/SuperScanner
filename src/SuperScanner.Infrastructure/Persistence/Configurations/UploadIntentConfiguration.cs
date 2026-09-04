using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.Uploads;

namespace SuperScanner.Infrastructure.Persistence.Configurations;

public sealed class UploadIntentConfiguration : IEntityTypeConfiguration<UploadIntent>
{
    public void Configure(EntityTypeBuilder<UploadIntent> builder)
    {
        builder.ToTable("upload_intents");
        builder.HasKey(upload => upload.Id);
        builder.Property(upload => upload.OwnerFirebaseUid).HasMaxLength(128).IsRequired();
        builder.Property(upload => upload.QuarantineObjectKey).HasMaxLength(1024).IsRequired();
        builder.Property(upload => upload.DeclaredMediaType).HasMaxLength(128).IsRequired();
        builder.Property(upload => upload.DeclaredSizeBytes).IsRequired();
        builder.Property(upload => upload.DeclaredSha256Hex).HasMaxLength(64).IsRequired();
        builder.Property(upload => upload.ExpiresAt).IsRequired();
        builder.Property(upload => upload.State).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(upload => upload.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(upload => upload.ValidationErrorCode).HasMaxLength(64);
        builder.HasIndex(upload => upload.IdempotencyKey).IsUnique();
        builder.HasIndex(upload => new { upload.OwnerFirebaseUid, upload.DocumentId });
        builder
            .HasOne<Document>()
            .WithMany()
            .HasForeignKey(upload => upload.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder
            .HasOne<Page>()
            .WithMany()
            .HasForeignKey(upload => upload.PageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
