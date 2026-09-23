using Microsoft.EntityFrameworkCore;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.TextEditing;
using SuperScanner.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SuperScanner.Infrastructure.IntegrationTests.Persistence;

public sealed class TextEditPersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => postgres.StartAsync();

    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Round_trip_preserves_json_values_without_exposing_sensitive_text_in_ToString()
    {
        var options = Options();
        var seed = BuildSeed();
        var edit = CreateEdit(seed, "apply-1");

        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            db.Documents.Add(seed.Document);
            db.FontCatalogueEntries.Add(seed.Font);
            await db.SaveChangesAsync();
            db.PageRevisions.Add(seed.SourceRevision);
            await db.SaveChangesAsync();
            db.TextEditOperations.Add(edit);
            await db.SaveChangesAsync();
        }

        await using var reader = new AppDbContext(options);
        var stored = await reader.TextEditOperations.AsNoTracking().SingleAsync();

        Assert.Equal(edit.SelectedOcrElementIds, stored.SelectedOcrElementIds);
        Assert.Equal(edit.ReplacementBox, stored.ReplacementBox);
        Assert.Equal(edit.Style, stored.Style);
        Assert.DoesNotContain(edit.OriginalText, stored.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(edit.ReplacementText, stored.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Duplicate_page_idempotency_key_is_rejected()
    {
        await using var db = new AppDbContext(Options());
        await db.Database.MigrateAsync();
        var seed = BuildSeed();
        db.Documents.Add(seed.Document);
        db.FontCatalogueEntries.Add(seed.Font);
        await db.SaveChangesAsync();
        db.PageRevisions.Add(seed.SourceRevision);
        await db.SaveChangesAsync();
        db.TextEditOperations.AddRange(
            CreateEdit(seed, "same-key"),
            CreateEdit(seed, "same-key"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Concurrent_state_transition_is_rejected()
    {
        var options = Options();
        var seed = BuildSeed();
        var edit = CreateEdit(seed, "apply-1");
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            db.Documents.Add(seed.Document);
            db.FontCatalogueEntries.Add(seed.Font);
            await db.SaveChangesAsync();
            db.PageRevisions.Add(seed.SourceRevision);
            await db.SaveChangesAsync();
            db.TextEditOperations.Add(edit);
            await db.SaveChangesAsync();
        }

        await using var first = new AppDbContext(options);
        await using var second = new AppDbContext(options);
        var firstEdit = await first.TextEditOperations.SingleAsync(candidate => candidate.Id == edit.Id);
        var secondEdit = await second.TextEditOperations.SingleAsync(candidate => candidate.Id == edit.Id);
        firstEdit.Start(Now.AddSeconds(1));
        secondEdit.Start(Now.AddSeconds(2));

        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task One_result_revision_cannot_complete_two_edits()
    {
        var options = Options();
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();
        var seed = BuildSeed();
        db.Documents.Add(seed.Document);
        db.FontCatalogueEntries.Add(seed.Font);
        await db.SaveChangesAsync();
        db.PageRevisions.Add(seed.SourceRevision);
        await db.SaveChangesAsync();
        var first = CreateEdit(seed, "apply-1");
        first.Start(Now.AddSeconds(1));
        first.Complete(seed.SourceRevision.Id, Now.AddSeconds(2));
        db.TextEditOperations.Add(first);
        await db.SaveChangesAsync();

        await using var conflicting = new AppDbContext(options);
        var second = CreateEdit(seed, "apply-2");
        second.Start(Now.AddSeconds(1));
        second.Complete(seed.SourceRevision.Id, Now.AddSeconds(2));
        conflicting.TextEditOperations.Add(second);

        await Assert.ThrowsAsync<DbUpdateException>(() => conflicting.SaveChangesAsync());
    }

    [Fact]
    public async Task Parent_revision_cannot_be_deleted_while_a_child_exists()
    {
        var options = Options();
        var seed = BuildSeed();
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            db.Documents.Add(seed.Document);
            await db.SaveChangesAsync();
            db.PageRevisions.Add(seed.SourceRevision);
            await db.SaveChangesAsync();
            var producingEdit = CreateEdit(seed, "apply-parent-child");
            db.TextEditOperations.Add(producingEdit);
            await db.SaveChangesAsync();
            var child = PageRevision.CreateDerived(
                Guid.NewGuid(), seed.Page.Id, seed.SourceRevision.Id, producingEdit.Id,
                "page-revisions/child.jpg", Hash('d'), Now.AddSeconds(1));
            db.PageRevisions.Add(child);
            await db.SaveChangesAsync();
        }

        await using var deletion = new AppDbContext(options);
        var parent = await deletion.PageRevisions.SingleAsync(
            revision => revision.Id == seed.SourceRevision.Id);
        deletion.PageRevisions.Remove(parent);

        await Assert.ThrowsAsync<DbUpdateException>(() => deletion.SaveChangesAsync());
    }

    private DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;

    private static TextEditOperation CreateEdit(Seed seed, string idempotencyKey) =>
        TextEditOperation.Queue(
            Guid.NewGuid(), seed.Document.Id, seed.Page.Id, "owner",
            seed.SourceRevision.Id, Guid.NewGuid(), [Guid.NewGuid(), Guid.NewGuid()],
            "Yap Tzing Yeow", "Tan BB", new NormalizedBox(0.1, 0.2, 0.3, 0.1),
            new TextEditStyle("noto-sans", "v1", 0.04, 400, "#112233", 0,
                0.25, 0, TextAlignment.Left),
            1, null, idempotencyKey, Hash('c'), "renderer-v1", "layout-v1", Now);

    private static Seed BuildSeed()
    {
        var document = Document.Create(Guid.NewGuid(), "owner", "Document", Now);
        var page = document.AddPage(Guid.NewGuid(), 10, Now);
        page.MarkImportReady("page-sources/source.jpg", "image/jpeg");
        page.SetPreview("previews/page.jpg", "thumbnails/page.jpg");
        page.MarkReady();
        document.MarkReady(Now);
        var revision = PageRevision.CreateBase(
            Guid.NewGuid(), page.Id, page.PreviewObjectKey!, Hash('a'), Now);
        var font = FontCatalogueEntry.Create(
            Guid.NewGuid(), "noto-sans", "v1", "Noto Sans", "Noto Sans",
            Hash('b'), "OFL-1.1", "assets/noto-sans.woff2", "assets/noto-sans.ttf", true);
        return new(document, page, revision, font);
    }

    private static string Hash(char value) => new(value, 64);

    private sealed record Seed(
        Document Document,
        Page Page,
        PageRevision SourceRevision,
        FontCatalogueEntry Font);

    private static readonly DateTimeOffset Now =
        new(2026, 9, 23, 2, 0, 0, TimeSpan.Zero);
}
