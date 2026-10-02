using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ArksScanner.Infrastructure.Signatures;

namespace ArksScanner.Infrastructure.Persistence.Configurations;

public sealed class SignatureAssetWriteIntentConfiguration : IEntityTypeConfiguration<SignatureAssetWriteIntent>
{
    public void Configure(EntityTypeBuilder<SignatureAssetWriteIntent> builder)
    {
        builder.ToTable("signature_asset_write_intents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AssetKey).HasMaxLength(1024).IsRequired();
        builder.HasIndex(x => x.CreatedAt);
        // No cascading FK: journal must survive deletion of its document.
    }
}
