using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SuperScanner.Application.Abstractions;
using SuperScanner.Application.TextEditing;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.TextEditing;
using SuperScanner.Infrastructure.Persistence;
using SuperScanner.Infrastructure.Auditing;
using SuperScanner.Infrastructure.TextEditing;
using Testcontainers.PostgreSql;

namespace SuperScanner.Infrastructure.IntegrationTests.Persistence;

public sealed class TextRevisionSwitchPersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres =
        new PostgreSqlBuilder("postgres:17-alpine").Build();
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 0, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync() => postgres.StartAsync();
    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Undo_persists_active_revision_and_rejects_outsider()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options;
        var document = Document.Create(Guid.NewGuid(), "owner", "Test", Now);
        var page = document.AddPage(Guid.NewGuid(), 10, Now);
        page.MarkImportReady("original.jpg", "image/jpeg");
        page.SetPreview("base.jpg", "thumb.jpg");
        page.MarkReady();
        document.MarkReady(Now);
        var baseRevision = PageRevision.CreateBase(Guid.NewGuid(), page.Id,
            "base.jpg", new string('a', 64), Now);
        var edit = TextEditOperation.Queue(Guid.NewGuid(), document.Id, page.Id,
            "owner", baseRevision.Id, Guid.NewGuid(), [Guid.NewGuid()],
            "Name", "Tan BB", new NormalizedBox(.1, .2, .3, .1),
            new TextEditStyle("noto-sans", "archive-main-regular", .04, 400,
                "#000000", 0, .25, 0, TextAlignment.Left), 1, null,
            "key-1", new string('b', 64), "renderer-v1", "layout-v1", Now);
        var derived = PageRevision.CreateDerived(Guid.NewGuid(), page.Id,
            baseRevision.Id, edit.Id, "derived.jpg", new string('c', 64), Now);
        edit.Start(Now);
        await using (var seed = new AppDbContext(options))
        {
            await seed.Database.MigrateAsync();
            seed.Documents.Add(document);
            await seed.SaveChangesAsync();
            seed.PageRevisions.Add(baseRevision);
            seed.TextEditOperations.Add(edit);
            await seed.SaveChangesAsync();
            seed.PageRevisions.Add(derived);
            await seed.SaveChangesAsync();
            edit.Complete(derived.Id, Now);
            page.ActivateRevision(derived);
            await seed.SaveChangesAsync();
        }

        await using (var db = new AppDbContext(options))
        {
            var command = new SwitchPageRevision(new EfTextEditRepository(db),
                new HmacAuditWriter(db, Options.Create(new AuditOptions
                {
                    SigningKeyBase64 = Convert.ToBase64String(new byte[32]),
                    SigningKeyId = "test"
                })), new Clock());
            await Assert.ThrowsAsync<TextSelectionNotFoundException>(() =>
                command.HandleAsync(new SwitchPageRevisionRequest("outsider",
                    document.Id, page.Id, derived.Id, RevisionSwitchDirection.Undo), default));
            await command.HandleAsync(new SwitchPageRevisionRequest("owner",
                document.Id, page.Id, derived.Id, RevisionSwitchDirection.Undo), default);
        }
        await using var verify = new AppDbContext(options);
        Assert.Equal(baseRevision.Id, (await verify.Pages.SingleAsync(p => p.Id == page.Id)).ActiveRevisionId);
        var audit = Assert.Single(await verify.AuditEvents
            .Where(evt => evt.Action == "text_edit.undo").ToListAsync());
        Assert.DoesNotContain("Tan BB", audit.RegionJson);
        var history = await new GetPageEditHistory(new EfTextEditRepository(verify))
            .HandleAsync("owner", document.Id, page.Id, default);
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);
    }

    private sealed class Clock : IClock { public DateTimeOffset UtcNow => Now; }
}
