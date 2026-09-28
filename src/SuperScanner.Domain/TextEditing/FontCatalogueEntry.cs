namespace SuperScanner.Domain.TextEditing;

public enum FontCategory { SansSerif, Serif, Monospace, Handwriting }
public enum FontFaceStyle { Normal, Italic }

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
    public FontCategory Category { get; private set; }
    public int Weight { get; private set; }
    public FontFaceStyle Style { get; private set; }
    public string WebFamilyName { get; private set; } = string.Empty;
    public bool SelectableForNewEdits { get; private set; }

    public bool SupportsWeight(int weight) => weight == Weight;

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
        bool enabled,
        FontCategory category = FontCategory.SansSerif,
        int weight = 400,
        FontFaceStyle style = FontFaceStyle.Normal,
        string? webFamilyName = null,
        bool? selectableForNewEdits = null)
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
        if (!Enum.IsDefined(category)) throw new ArgumentOutOfRangeException(nameof(category));
        if (weight is < 100 or > 900 || weight % 100 != 0)
            throw new ArgumentOutOfRangeException(nameof(weight));
        if (!Enum.IsDefined(style)) throw new ArgumentOutOfRangeException(nameof(style));
        webFamilyName ??= familyName;
        if (webFamilyName.Length is < 1 or > 120 ||
            webFamilyName.Any(character => !char.IsLetterOrDigit(character) && character is not (' ' or '-')))
            throw new ArgumentException("A safe web font family name is required.", nameof(webFamilyName));
        if (selectableForNewEdits == true && !enabled)
            throw new ArgumentException("An unavailable font cannot be selected for new edits.", nameof(selectableForNewEdits));

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
            Enabled = enabled,
            Category = category,
            Weight = weight,
            Style = style,
            WebFamilyName = webFamilyName,
            SelectableForNewEdits = selectableForNewEdits ?? enabled
        };
    }
}
