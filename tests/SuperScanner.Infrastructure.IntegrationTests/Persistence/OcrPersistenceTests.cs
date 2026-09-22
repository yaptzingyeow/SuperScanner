using Microsoft.EntityFrameworkCore;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.Ocr;
using SuperScanner.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SuperScanner.Infrastructure.IntegrationTests.Persistence;

public sealed class OcrPersistenceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);
    private static readonly OcrPoint[] Polygon =
    [
        new(.1, .1), new(.9, .1), new(.9, .2), new(.1, .2)
    ];
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => postgres.StartAsync();

    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task RoundTrip_PersistsJsonbHierarchyAndEnforcesOneResultPerSource()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
        var (document, page) = CreateReadyDocument();
        var result = CreateReadyResult(page);
        db.AddRange(document, result);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var stored = await db.PageOcrResults.Include(candidate => candidate.Elements).SingleAsync();

        Assert.Equal(OcrResultState.Ready, stored.State);
        Assert.Equal("Name", stored.FullText);
        Assert.Collection(stored.Elements.OrderBy(element => element.Kind),
            block => Assert.Equal(OcrElementKind.Block, block.Kind),
            word =>
            {
                Assert.Equal(OcrElementKind.Word, word.Kind);
                Assert.Equal(Polygon, word.Polygon);
            });

        db.PageOcrResults.Add(PageOcrResult.Queue(Guid.NewGuid(), page.Id,
            result.SourceObjectKey, result.SourceFingerprint, "en", Now));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Migration_UsesJsonbAndExpectedIndexes()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT data_type
            FROM information_schema.columns
            WHERE table_name = 'ocr_elements' AND column_name = 'PolygonJson';

            SELECT indexname
            FROM pg_indexes
            WHERE tablename = 'page_ocr_results'
            ORDER BY indexname;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("jsonb", reader.GetString(0));
        Assert.True(await reader.NextResultAsync());
        var indexes = new List<string>();
        while (await reader.ReadAsync()) indexes.Add(reader.GetString(0));
        Assert.Contains("IX_page_ocr_results_PageId_SourceFingerprint", indexes);
        Assert.Contains("IX_page_ocr_results_PageId_State_QueuedAt", indexes);
    }

    [Fact]
    public async Task RemovingResult_CascadesItsElements()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
        var (document, page) = CreateReadyDocument();
        var result = CreateReadyResult(page);
        db.AddRange(document, result);
        await db.SaveChangesAsync();

        db.PageOcrResults.Remove(result);
        await db.SaveChangesAsync();

        Assert.Empty(await db.OcrElements.ToListAsync());
    }

    [Fact]
    public async Task Repository_ReturnsOnlyOwnedActiveCurrentSource()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
        var (document, page) = CreateReadyDocument();
        var result = CreateReadyResult(page);
        db.AddRange(document, result);
        await db.SaveChangesAsync();
        var repository = new EfOcrRepository(db);

        OcrPageSource? source;
        await using (var transaction = await repository.BeginTransactionAsync(default))
        {
            source = await repository.FindOwnedReadySourceAsync(
                "owner-a", document.Id, page.Id, default);
            await transaction.CommitAsync(default);
        }
        var current = await repository.FindCurrentOwnedAsync(
            "owner-a", document.Id, page.Id, default);
        var unowned = await repository.FindCurrentOwnedAsync(
            "owner-b", document.Id, page.Id, default);

        Assert.Equal(new(page.Id, page.PreviewObjectKey!, "image/jpeg"), source);
        Assert.Equal(result.Id, current?.Id);
        Assert.Null(unowned);

        document.RemovePage(page.Id, "owner-a", Now.AddMinutes(1));
        await db.SaveChangesAsync();
        Assert.Null(await repository.FindCurrentOwnedAsync(
            "owner-a", document.Id, page.Id, default));
    }

    [Fact]
    public async Task Repository_DoesNotReturnResultForSupersededPreview()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
        var (document, page) = CreateReadyDocument();
        var result = CreateReadyResult(page);
        db.AddRange(document, result);
        await db.SaveChangesAsync();
        page.SetPreview("previews/document/page/crop-2.jpg", "thumbnails/document/page/crop-2.jpg");
        await db.SaveChangesAsync();

        var repository = new EfOcrRepository(db);

        Assert.Null(await repository.FindCurrentOwnedAsync(
            "owner-a", document.Id, page.Id, default));
    }

    private AppDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options);

    private static (Document Document, Page Page) CreateReadyDocument()
    {
        var document = Document.Create(Guid.NewGuid(), "owner-a", "OCR form", Now);
        var page = document.AppendImportedPages(Guid.NewGuid(), [1], 50, Now).Single();
        page.MarkImportReady("page-sources/document/page/source.jpg", "image/jpeg");
        page.SetPreview("previews/document/page/crop-1.jpg", "thumbnails/document/page/crop-1.jpg");
        page.MarkReady();
        document.MarkReady(Now);
        return (document, page);
    }

    private static PageOcrResult CreateReadyResult(Page page)
    {
        var result = PageOcrResult.Queue(Guid.NewGuid(), page.Id, page.PreviewObjectKey!,
            new string('a', 64), "en", Now);
        var block = OcrElement.Create(Guid.NewGuid(), result.Id, null,
            OcrElementKind.Block, "Name", .9, OcrTextType.Printed, 0, Polygon);
        var word = OcrElement.Create(Guid.NewGuid(), result.Id, block.Id,
            OcrElementKind.Word, "Name", .95, OcrTextType.Printed, 0, Polygon);
        result.BeginAttempt(1, Now.AddSeconds(1));
        result.Complete("Fake", "fixture-v1", "Name", [block, word], Now.AddSeconds(2));
        return result;
    }
}
