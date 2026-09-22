using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SuperScanner.Domain.Documents;
using SuperScanner.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SuperScanner.Infrastructure.IntegrationTests.Persistence;

public sealed class DocumentPersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task SavesAndFiltersDocumentByOwner()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using (var writer = new AppDbContext(options))
        {
            await writer.Database.MigrateAsync();
            writer.Documents.Add(Document.Create(Guid.NewGuid(), "owner-a", "Form", DateTimeOffset.UtcNow));
            writer.Documents.Add(Document.Create(Guid.NewGuid(), "owner-b", "Private", DateTimeOffset.UtcNow));
            await writer.SaveChangesAsync();
        }

        await using var reader = new AppDbContext(options);
        var owned = await reader.Documents
            .AsNoTracking()
            .Where(document => document.OwnerFirebaseUid == "owner-a")
            .ToListAsync();

        Assert.Single(owned);
        Assert.Equal("Form", owned[0].Title);
    }

    [Fact]
    public async Task MigrationAddsBoundedCropProvenanceColumns()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT column_name, character_maximum_length
            FROM information_schema.columns
            WHERE table_name = 'pages'
              AND column_name IN ('CropModelVersion', 'CropDiagnosticsCode')
            ORDER BY column_name
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new Dictionary<string, int?>();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetInt32(1));
        }

        Assert.Equal(64, columns["CropDiagnosticsCode"]);
        Assert.Equal(100, columns["CropModelVersion"]);
    }

    [Fact]
    public async Task SavesExportSnapshotAndOrderedPageMetadata()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
        var document = SeedReadyDocument();
        var export = DocumentExport.Create(
            Guid.NewGuid(),
            document,
            document.OwnerFirebaseUid,
            Now,
            TimeSpan.FromDays(7));
        db.AddRange(document, export);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var storedDocument = await db.Documents
            .Include(candidate => candidate.Pages)
            .SingleAsync();
        var storedExport = await db.DocumentExports.SingleAsync();

        Assert.Equal(document.Revision, storedDocument.Revision);
        Assert.Equal(document.PageOrderRevision, storedDocument.PageOrderRevision);
        Assert.Collection(
            storedDocument.ActivePages,
            first =>
            {
                Assert.Equal(1, first.Position);
                Assert.Equal(1, first.SourcePageIndex);
                Assert.Equal("image/png", first.OriginalMediaType);
                Assert.Equal(PageState.Ready, first.State);
            },
            second =>
            {
                Assert.Equal(2, second.Position);
                Assert.Equal(2, second.SourcePageIndex);
                Assert.Equal("image/png", second.OriginalMediaType);
                Assert.Equal(PageState.Ready, second.State);
            });
        Assert.Equal(document.Revision, storedExport.DocumentRevision);
        Assert.Equal(2, storedExport.ReadyPageCount);
        var snapshot = JsonSerializer.Deserialize<DocumentExportSnapshotEntry[]>(storedExport.SnapshotJson);
        Assert.NotNull(snapshot);
        Assert.Equal(
            [
                new DocumentExportSnapshotEntry(
                    document.ActivePages.ElementAt(0).Id,
                    1,
                    0,
                    ScanFilter.Default,
                    $"previews/{document.ActivePages.ElementAt(0).Id:N}.png"),
                new DocumentExportSnapshotEntry(
                    document.ActivePages.ElementAt(1).Id,
                    2,
                    0,
                    ScanFilter.Default,
                    $"previews/{document.ActivePages.ElementAt(1).Id:N}.png")
            ],
            snapshot);
    }

    [Fact]
    public async Task RoundTripsFailedAndSoftRemovedPageMetadata()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
        var document = Document.Create(Guid.NewGuid(), "owner-a", "Form", Now);
        var page = document.AppendImportedPages(Guid.NewGuid(), [1], 10, Now).Single();
        page.MarkImportReady("page-sources/document/page/source.png", "image/png");
        page.MarkFailed("import-decode-failed");
        document.RemovePage(page.Id, "owner-a", Now.AddMinutes(1));
        db.Add(document);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var stored = await db.Pages.SingleAsync();

        Assert.Equal(PageState.Failed, stored.State);
        Assert.Equal("import-decode-failed", stored.FailureCode);
        Assert.Equal(Now.AddMinutes(1), stored.RemovedAt);
        Assert.Equal("owner-a", stored.RemovedByFirebaseUid);
    }

    [Fact]
    public async Task RejectsDuplicateActivePagePositions()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
        var document = Document.Create(Guid.NewGuid(), "owner-a", "Form", Now);
        var pages = document.AppendImportedPages(Guid.NewGuid(), [1, 2], 10, Now);
        db.Add(document);
        db.Entry(pages[1]).Property(page => page.Position).CurrentValue = pages[0].Position;

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task RejectsDuplicateSourcePageTuples()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
        var document = Document.Create(Guid.NewGuid(), "owner-a", "Form", Now);
        var pages = document.AppendImportedPages(Guid.NewGuid(), [1, 2], 10, Now);
        db.Add(document);
        db.Entry(pages[1]).Property(page => page.SourcePageIndex).CurrentValue = pages[0].SourcePageIndex;

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options);

    private static Document SeedReadyDocument()
    {
        var document = Document.Create(Guid.NewGuid(), "owner-a", "Form", Now);
        foreach (var page in document.AppendImportedPages(Guid.NewGuid(), [1, 2], 10, Now))
        {
            page.MarkImportReady($"page-sources/{page.Id:N}/source.png", "image/png");
            page.SetPreview($"previews/{page.Id:N}.png", $"thumbnails/{page.Id:N}.png");
            page.MarkReady();
        }

        document.MarkReady(Now);
        return document;
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
}

public sealed class MultiPageDocumentsMigrationScriptTests
{
    [Fact]
    public void GeneratesExplicitCropStatusBackfillPrecedence()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;
        using var db = new AppDbContext(options);
        var script = db.GetService<IMigrator>().GenerateScript(
            "20260914000000_AiDocumentBoundary",
            "20260914210000_MultiPageDocuments");

        var previewReadyIndex = script.IndexOf(
            "WHEN p.\"PreviewObjectKey\" IS NOT NULL THEN 'Ready'",
            StringComparison.Ordinal);
        Assert.True(previewReadyIndex >= 0);

        var cropStatusClauseIndexes = new[]
        {
            script.IndexOf("WHEN p.\"CropStatus\" = 'NeedsCrop' THEN 'NeedsCrop'", StringComparison.Ordinal),
            script.IndexOf("WHEN p.\"CropStatus\" IN ('Detecting', 'Processing') THEN 'Processing'", StringComparison.Ordinal),
            script.IndexOf("WHEN p.\"CropStatus\" = 'Failed' THEN 'Failed'", StringComparison.Ordinal)
        };

        Assert.All(cropStatusClauseIndexes, index => Assert.InRange(index, 0, previewReadyIndex - 1));
    }
}
