using Microsoft.EntityFrameworkCore;
using ArksScanner.Application.Ocr;
using ArksScanner.Domain.Documents;
using ArksScanner.Domain.Ocr;
using ArksScanner.Infrastructure.Persistence;
using ArksScanner.Infrastructure.TextEditing;
using Testcontainers.PostgreSql;

namespace ArksScanner.Infrastructure.IntegrationTests.Persistence;

public sealed class TextSelectionRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => postgres.StartAsync();
    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Only_owner_can_load_current_page_and_ocr_words()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options;
        var now = new DateTimeOffset(2026, 9, 23, 0, 0, 0, TimeSpan.Zero);
        var document = Document.Create(Guid.NewGuid(), "owner", "Document", now);
        var page = document.AddPage(Guid.NewGuid(), 1, now);
        page.MarkImportReady("source/original.jpg", "image/jpeg");
        page.SetPreview("previews/page.jpg", "thumbnails/page.jpg");
        page.MarkReady();
        document.MarkReady(now);
        var result = PageOcrResult.Queue(Guid.NewGuid(), page.Id, page.PreviewObjectKey!,
            OcrSourceFingerprint.Create(page.PreviewObjectKey!), "en", now);
        result.BeginAttempt(1, now);
        var line = OcrElement.Create(Guid.NewGuid(), result.Id, null, OcrElementKind.Line,
            "Name", 0.9, OcrTextType.Printed, 0,
            [new(.1, .1), new(.3, .1), new(.3, .2), new(.1, .2)]);
        var word = OcrElement.Create(Guid.NewGuid(), result.Id, line.Id, OcrElementKind.Word,
            "Name", 0.9, OcrTextType.Printed, 0,
            [new(.1, .1), new(.3, .1), new(.3, .2), new(.1, .2)]);
        result.Complete("test", "v1", "Name", [line, word], now);

        await using (var seed = new AppDbContext(options))
        {
            await seed.Database.MigrateAsync();
            seed.Documents.Add(document);
            seed.PageOcrResults.Add(result);
            await seed.SaveChangesAsync();
        }

        await using var reader = new AppDbContext(options);
        var repository = new EfTextEditRepository(reader);
        Assert.Null(await repository.FindOwnedAsync("outsider", document.Id, page.Id, result.Id, default));
        var owned = await repository.FindOwnedAsync("owner", document.Id, page.Id, result.Id, default);
        Assert.NotNull(owned);
        Assert.Equal(page.PreviewObjectKey, owned.SourceObjectKey);
        Assert.Contains(owned.Elements, element => element.Id == word.Id);
    }
}
