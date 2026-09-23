using System.Security.Cryptography;
using SuperScanner.Infrastructure.TextEditing;

namespace SuperScanner.Application.Tests.TextEditing;

public sealed class BundledFontCatalogueTests
{
    [Fact]
    public void Manifest_hashes_match_every_bundled_font_and_licence_is_present()
    {
        var root = FindRepositoryRoot();
        var catalogue = BundledFontCatalogue.Load(Path.Combine(root, "assets", "fonts", "manifest.json"));
        var licence = File.ReadAllText(Path.Combine(root, "assets", "fonts", "OFL.txt"));

        Assert.NotEmpty(catalogue.Entries);
        Assert.Contains("SIL OPEN FONT LICENSE Version 1.1", licence, StringComparison.OrdinalIgnoreCase);
        foreach (var entry in catalogue.Entries.Where(entry => entry.Enabled))
        {
            var bytes = File.ReadAllBytes(Path.Combine(root, entry.RendererAssetPath));
            Assert.Equal(entry.AssetSha256Hex, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            Assert.Equal("OFL-1.1", entry.LicenseIdentifier);
            Assert.Same(entry, catalogue.Get(entry.CatalogueId, entry.Version));
        }
    }

    [Fact]
    public void Unknown_or_disabled_font_cannot_be_resolved()
    {
        var root = FindRepositoryRoot();
        var catalogue = BundledFontCatalogue.Load(Path.Combine(root, "assets", "fonts", "manifest.json"));

        Assert.Throws<KeyNotFoundException>(() => catalogue.Get("unknown-font", "v1"));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SuperScanner.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
