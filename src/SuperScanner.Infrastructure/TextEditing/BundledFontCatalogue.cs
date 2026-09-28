using System.Security.Cryptography;
using System.Text.Json;
using SuperScanner.Domain.TextEditing;

namespace SuperScanner.Infrastructure.TextEditing;

public interface IFontCatalogue
{
    IReadOnlyList<FontCatalogueEntry> Entries { get; }
    FontCatalogueEntry Get(string catalogueId, string version);
}

public sealed class BundledFontCatalogue : IFontCatalogue
{
    private readonly Dictionary<(string, string), FontCatalogueEntry> entriesByKey;

    private BundledFontCatalogue(List<FontCatalogueEntry> entries)
    {
        Entries = entries.AsReadOnly();
        entriesByKey = entries.Where(entry => entry.Enabled)
            .ToDictionary(entry => (entry.CatalogueId, entry.Version));
    }

    public IReadOnlyList<FontCatalogueEntry> Entries { get; }

    public FontCatalogueEntry Get(string catalogueId, string version) =>
        entriesByKey.TryGetValue((catalogueId, version), out var entry)
            ? entry
            : throw new KeyNotFoundException($"Font {catalogueId}/{version} is unavailable.");

    public static BundledFontCatalogue Load(string manifestPath)
    {
        var fullManifestPath = Path.GetFullPath(manifestPath);
        var fontsDirectory = Path.GetDirectoryName(fullManifestPath)
            ?? throw new InvalidOperationException("Font manifest directory is missing.");
        var root = Directory.GetParent(fontsDirectory)?.Parent?.FullName
            ?? throw new InvalidOperationException("Font asset root is missing.");
        using var document = JsonDocument.Parse(File.ReadAllText(fullManifestPath));
        var faces = document.RootElement.GetProperty("faces");
        var entries = new List<FontCatalogueEntry>();
        foreach (var face in faces.EnumerateArray())
        {
            string Field(string name) => face.GetProperty(name).GetString()
                ?? throw new InvalidOperationException($"Font {name} is missing.");
            var assetPath = Field("rendererAssetPath");
            var webPath = Field("webAssetPath");
            var fullAssetPath = Path.GetFullPath(Path.Combine(root, assetPath));
            var expectedDirectory = Path.GetFullPath(fontsDirectory) + Path.DirectorySeparatorChar;
            if (!fullAssetPath.StartsWith(expectedDirectory, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFullPath(Path.Combine(root, webPath)) != fullAssetPath)
                throw new InvalidOperationException("Font asset path escapes the pinned font directory.");
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fullAssetPath))).ToLowerInvariant();
            var expectedHash = Field("assetSha256Hex").ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(hash), Convert.FromHexString(expectedHash)))
                throw new InvalidOperationException($"Font asset hash mismatch: {assetPath}.");
            var license = Field("licenseIdentifier");
            var noticePath = Path.GetFullPath(Path.Combine(root, Field("licenseNoticePath")));
            if (license != "OFL-1.1" ||
                !noticePath.StartsWith(expectedDirectory, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(noticePath) ||
                !File.ReadAllText(noticePath)
                    .Contains("SIL OPEN FONT LICENSE Version 1.1", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Bundled font licence is invalid.");
            entries.Add(FontCatalogueEntry.Create(
                Guid.NewGuid(), Field("catalogueId"), Field("version"), Field("displayName"),
                Field("familyName"), expectedHash, license, webPath, assetPath,
                face.GetProperty("enabled").GetBoolean(),
                Enum.Parse<FontCategory>(Field("category"), ignoreCase: false),
                face.GetProperty("weight").GetInt32(),
                Enum.Parse<FontFaceStyle>(Field("style"), ignoreCase: false),
                Field("webFamilyName"),
                face.GetProperty("selectableForNewEdits").GetBoolean()));
        }
        if (entries.Count == 0 || entries.Select(entry => (entry.CatalogueId, entry.Version)).Distinct().Count() != entries.Count)
            throw new InvalidOperationException("Font manifest is empty or contains duplicate faces.");
        return new BundledFontCatalogue(entries);
    }
}
