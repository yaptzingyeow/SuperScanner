namespace SuperScanner.Domain.TextEditing;

public sealed class FontCatalogueEntry
{
    private FontCatalogueEntry()
    {
    }

    public Guid Id { get; private set; }
    public string CatalogueId { get; private set; } = string.Empty;
    public string Version { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string FamilyName { get; private set; } = string.Empty;
    public string AssetSha256Hex { get; private set; } = string.Empty;
    public string LicenseIdentifier { get; private set; } = string.Empty;
    public string WebAssetPath { get; private set; } = string.Empty;
    public string RendererAssetPath { get; private set; } = string.Empty;
    public bool Enabled { get; private set; }

    public bool SupportsWeight(int weight) =>
        weight == (Version.Contains("bold", StringComparison.OrdinalIgnoreCase) ? 700 : 400);

    public static FontCatalogueEntry Create(
        Guid id,
        string catalogueId,
        string version,
        string displayName,
        string familyName,
        string assetSha256Hex,
        string licenseIdentifier,
        string webAssetPath,
        string rendererAssetPath,
        bool enabled)
    {
        if (id == Guid.Empty) throw new ArgumentException("A font entry ID is required.", nameof(id));
        TextEditStyle.ValidateIdentifier(catalogueId, nameof(catalogueId));
        TextEditStyle.ValidateIdentifier(version, nameof(version));
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(familyName);
        if (assetSha256Hex.Length != 64 || assetSha256Hex.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("A SHA-256 font asset hash is required.", nameof(assetSha256Hex));
        ArgumentException.ThrowIfNullOrWhiteSpace(licenseIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(webAssetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(rendererAssetPath);

        return new FontCatalogueEntry
        {
            Id = id,
            CatalogueId = catalogueId,
            Version = version,
            DisplayName = displayName,
            FamilyName = familyName,
            AssetSha256Hex = assetSha256Hex.ToLowerInvariant(),
            LicenseIdentifier = licenseIdentifier,
            WebAssetPath = webAssetPath,
            RendererAssetPath = rendererAssetPath,
            Enabled = enabled
        };
    }
}
