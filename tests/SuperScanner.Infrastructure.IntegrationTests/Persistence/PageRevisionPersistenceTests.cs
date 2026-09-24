using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.TextEditing;
using SuperScanner.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SuperScanner.Infrastructure.IntegrationTests.Persistence;

public sealed class PageRevisionPersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => postgres.StartAsync();

    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Round_trip_preserves_parent_link_and_active_revision()
    {
        var options = Options();
        Guid pageId;
        Guid derivedId;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            var document = ReadyDocument();
            var page = document.ActivePages.Single();
            pageId = page.Id;
            db.Documents.Add(document);
            await db.SaveChangesAsync();

            var baseRevision = PageRevision.CreateBase(
                Guid.NewGuid(), page.Id, page.PreviewObjectKey!, Hash('a'), Now);
            db.PageRevisions.Add(baseRevision);
            await db.SaveChangesAsync();
            var producingEdit = TextEditOperation.Queue(
                Guid.NewGuid(), document.Id, page.Id, "owner",
                baseRevision.Id, Guid.NewGuid(), [Guid.NewGuid()],
                "Before", "After", new NormalizedBox(0.1, 0.1, 0.2, 0.1),
                new TextEditStyle("noto-sans", "v1", 0.04, 400, "#000000", 0,
                    0.2, 0, TextAlignment.Left),
                1, null, "page-revision-test", Hash('c'), "renderer-v1", "layout-v1", Now);
            db.TextEditOperations.Add(producingEdit);
            await db.SaveChangesAsync();
            var derived = PageRevision.CreateDerived(
                Guid.NewGuid(), page.Id, baseRevision.Id, producingEdit.Id,
                $"page-revisions/{page.Id:N}/derived.jpg", Hash('b'), Now.AddSeconds(1));
            derivedId = derived.Id;
            db.PageRevisions.Add(derived);
            page.ActivateRevision(derived);
            await db.SaveChangesAsync();
        }

        await using var reader = new AppDbContext(options);
        var stored = await reader.Pages.Include(page => page.ActiveRevision)
            .SingleAsync(page => page.Id == pageId);
        var derivedRevision = await reader.PageRevisions.SingleAsync(revision => revision.Id == derivedId);

        Assert.Equal(derivedId, stored.ActiveRevisionId);
        Assert.Equal(derivedRevision.ObjectKey, stored.GetProcessedObjectKey());
        Assert.NotNull(derivedRevision.ParentRevisionId);
        Assert.NotNull(derivedRevision.ProducingTextEditId);
    }

    [Fact]
    public async Task Printed_text_migration_preserves_existing_legacy_page()
    {
        var options = Options();
        var document = ReadyDocument();
        await using (var old = new AppDbContext(options))
        {
            await old.GetService<IMigrator>().MigrateAsync("20260922174452_PageRevisions");
            old.Documents.Add(document);
            await old.SaveChangesAsync();
        }
        await using var upgraded = new AppDbContext(options);
        await upgraded.Database.MigrateAsync();
        var page = await upgraded.Pages.SingleAsync(candidate =>
            candidate.DocumentId == document.Id);
        Assert.Null(page.ActiveRevisionId);
        Assert.Equal("previews/page.jpg", page.GetProcessedObjectKey());
        Assert.Equal("previews/page.jpg", page.GetExportObjectKey());
        Assert.Contains("20260923005415_PrintedTextEditing",
            await upgraded.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task Concurrent_active_revision_switch_is_rejected()
    {
        var options = Options();
        var document = ReadyDocument();
        var page = document.ActivePages.Single();
        var firstRevision = PageRevision.CreateBase(
            Guid.NewGuid(), page.Id, page.PreviewObjectKey!, Hash('a'), Now);
        var secondRevision = PageRevision.CreateBase(
            Guid.NewGuid(), page.Id, "previews/alternate.jpg", Hash('b'), Now.AddSeconds(1));

        await using (var seed = new AppDbContext(options))
        {
            await seed.Database.MigrateAsync();
            seed.Documents.Add(document);
            await seed.SaveChangesAsync();
            seed.PageRevisions.AddRange(firstRevision, secondRevision);
            await seed.SaveChangesAsync();
        }

        await using var first = new AppDbContext(options);
        await using var second = new AppDbContext(options);
        var firstPage = await first.Pages.SingleAsync(candidate => candidate.Id == page.Id);
        var secondPage = await second.Pages.SingleAsync(candidate => candidate.Id == page.Id);
        var firstTarget = await first.PageRevisions.SingleAsync(candidate => candidate.Id == firstRevision.Id);
        var secondTarget = await second.PageRevisions.SingleAsync(candidate => candidate.Id == secondRevision.Id);

        firstPage.ActivateRevision(firstTarget);
        await first.SaveChangesAsync();
        secondPage.ActivateRevision(secondTarget);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Export_repository_loads_active_revision_for_snapshot()
    {
        var options = Options();
        var document = ReadyDocument();
        var page = document.ActivePages.Single();
        await using (var seed = new AppDbContext(options))
        {
            await seed.Database.MigrateAsync();
            seed.Documents.Add(document);
            await seed.SaveChangesAsync();
            var revision = PageRevision.CreateBase(Guid.NewGuid(), page.Id,
                "page-revisions/edited.jpg", Hash('a'), Now);
            seed.PageRevisions.Add(revision);
            page.ActivateRevision(revision);
            await seed.SaveChangesAsync();
        }

        await using var reader = new AppDbContext(options);
        var repository = new EfDocumentRepository(reader);
        await using var transaction = await repository.BeginTransactionAsync(default);
        var loaded = await repository.FindOwnedForUpdateAsync("owner", document.Id, default);
        var export = DocumentExport.Create(Guid.NewGuid(), loaded!, "owner", Now,
            TimeSpan.FromDays(1));
        Assert.Contains("page-revisions/edited.jpg", export.SnapshotJson);
    }

    private DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;

    private static Document ReadyDocument()
    {
        var document = Document.Create(Guid.NewGuid(), "owner", "Document", Now);
        var page = document.AddPage(Guid.NewGuid(), 10, Now);
        page.MarkImportReady("page-sources/source.jpg", "image/jpeg");
        page.SetPreview("previews/page.jpg", "thumbnails/page.jpg");
        page.MarkReady();
        document.MarkReady(Now);
        return document;
    }

    private static string Hash(char value) => new(value, 64);

    private static readonly DateTimeOffset Now =
        new(2026, 9, 23, 1, 0, 0, TimeSpan.Zero);
}
