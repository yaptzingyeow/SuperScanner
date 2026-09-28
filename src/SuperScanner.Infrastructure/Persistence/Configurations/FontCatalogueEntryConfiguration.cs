using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SuperScanner.Domain.TextEditing;

namespace SuperScanner.Infrastructure.Persistence.Configurations;

public sealed class FontCatalogueEntryConfiguration : IEntityTypeConfiguration<FontCatalogueEntry>
{
    public void Configure(EntityTypeBuilder<FontCatalogueEntry> builder)
    {
        builder.ToTable("font_catalogue_entries");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.CatalogueId).HasMaxLength(64).IsRequired();
        builder.Property(entry => entry.Version).HasMaxLength(64).IsRequired();
        builder.Property(entry => entry.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(entry => entry.FamilyName).HasMaxLength(200).IsRequired();
        builder.Property(entry => entry.AssetSha256Hex).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(entry => entry.LicenseIdentifier).HasMaxLength(64).IsRequired();
        builder.Property(entry => entry.WebAssetPath).HasMaxLength(1024).IsRequired();
        builder.Property(entry => entry.RendererAssetPath).HasMaxLength(1024).IsRequired();
        builder.Property(entry => entry.Enabled).IsRequired();
        builder.Ignore(entry => entry.Category);
        builder.Ignore(entry => entry.Weight);
        builder.Ignore(entry => entry.Style);
        builder.Ignore(entry => entry.WebFamilyName);
        builder.Ignore(entry => entry.SelectableForNewEdits);
        builder.HasIndex(entry => new { entry.CatalogueId, entry.Version }).IsUnique();
    }
}
