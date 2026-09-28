using System.Security.Cryptography;
using System.Text.Json.Nodes;
using SuperScanner.Domain.TextEditing;
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
            var webBytes = File.ReadAllBytes(Path.Combine(root, "apps", "web", "public", entry.WebAssetPath));
            Assert.Equal(entry.AssetSha256Hex,
                Convert.ToHexString(SHA256.HashData(webBytes)).ToLowerInvariant());
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

    [Fact]
    public void Noto_faces_expose_real_metadata_without_changing_existing_catalogue_ids()
    {
        var root = FindRepositoryRoot();
        var catalogue = BundledFontCatalogue.Load(Path.Combine(root, "assets", "fonts", "manifest.json"));

        var regular = catalogue.Get("noto-sans", "archive-main-regular");
        var bold = catalogue.Get("noto-sans", "archive-main-bold");
        Assert.Equal(FontCategory.SansSerif, regular.Category);
        Assert.Equal(400, regular.Weight);
        Assert.Equal(FontFaceStyle.Normal, regular.Style);
        Assert.Equal("SuperScanner Noto Sans v1", regular.WebFamilyName);
        Assert.True(regular.SelectableForNewEdits);
        Assert.True(regular.SupportsWeight(400));
        Assert.False(regular.SupportsWeight(700));
        Assert.Equal(700, bold.Weight);
        Assert.True(bold.SupportsWeight(700));
    }

    [Fact]
    public void Catalogue_contains_the_twenty_approved_font_families()
    {
        var root = FindRepositoryRoot();
        var catalogue = BundledFontCatalogue.Load(Path.Combine(root, "assets", "fonts", "manifest.json"));
        var actual = catalogue.Entries.Where(entry => entry.SelectableForNewEdits)
            .Select(entry => entry.CatalogueId).Distinct().Order(StringComparer.Ordinal).ToArray();
        var expected = new[] {
            "caladea", "carlito", "caveat", "dancing-script", "lato",
            "liberation-mono", "liberation-sans", "liberation-serif", "libre-baskerville",
            "merriweather", "montserrat", "noto-sans", "noto-sans-mono", "noto-serif",
            "open-sans", "oswald", "poppins", "roboto", "source-sans-3", "source-serif-4",
        };
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("assets/fonts/licenses/missing-OFL.txt")]
    [InlineData("../outside-OFL.txt")]
    public void Manifest_rejects_missing_or_escaping_face_licence_notice(string noticePath)
    {
        var root = FindRepositoryRoot();
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"superscanner-font-test-{Guid.NewGuid():N}");
        var fontsDirectory = Path.Combine(temporaryRoot, "assets", "fonts");
        Directory.CreateDirectory(fontsDirectory);
        try
        {
            File.Copy(Path.Combine(root, "assets", "fonts", "NotoSans-Regular.ttf"),
                Path.Combine(fontsDirectory, "NotoSans-Regular.ttf"));
            File.Copy(Path.Combine(root, "assets", "fonts", "OFL.txt"),
                Path.Combine(fontsDirectory, "OFL.txt"));
            var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "assets", "fonts", "manifest.json")))!;
            var faces = manifest["faces"]!.AsArray();
            while (faces.Count > 1) faces.RemoveAt(faces.Count - 1);
            faces[0]!["licenseNoticePath"] = noticePath;
            var path = Path.Combine(fontsDirectory, "manifest.json");
            File.WriteAllText(path, manifest.ToJsonString());

            Assert.Throws<InvalidOperationException>(() => BundledFontCatalogue.Load(path));
        }
        finally { Directory.Delete(temporaryRoot, recursive: true); }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SuperScanner.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
