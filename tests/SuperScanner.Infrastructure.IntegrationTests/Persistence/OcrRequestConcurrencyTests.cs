using Microsoft.EntityFrameworkCore;
using SuperScanner.Application.Abstractions;
using SuperScanner.Application.Ocr;
using SuperScanner.Domain.Documents;
using SuperScanner.Infrastructure.Persistence;
using SuperScanner.Infrastructure.Processing;
using Testcontainers.PostgreSql;

namespace SuperScanner.Infrastructure.IntegrationTests.Persistence;

public sealed class OcrRequestConcurrencyTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 1, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => postgres.StartAsync();

    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task ConcurrentRequests_CreateOneResultAndOneJob()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        Guid documentId;
        Guid pageId;
        await using (var seed = new AppDbContext(options))
        {
            await seed.Database.MigrateAsync();
            var document = Document.Create(Guid.NewGuid(), "owner", "OCR", Now);
            var page = document.AppendImportedPages(Guid.NewGuid(), [1], 50, Now).Single();
            page.MarkImportReady("page-sources/source.jpg", "image/jpeg");
            page.SetPreview("previews/document/page/crop-1.jpg", "thumbnails/document/page/crop-1.jpg");
            page.MarkReady();
            document.MarkReady(Now);
            seed.Add(document);
            await seed.SaveChangesAsync();
            documentId = document.Id;
            pageId = page.Id;
        }

        async Task<PageOcrDto> RequestAsync()
        {
            await using var db = new AppDbContext(options);
            var command = new RequestPageOcr(
                new EfOcrRepository(db),
                new PostgresJobQueue(db, new ConcurrencyClock(Now)),
                new ConcurrencyClock(Now));
            return await command.HandleAsync("owner", documentId, pageId, false, default);
        }

        var results = await Task.WhenAll(RequestAsync(), RequestAsync());

        Assert.Equal(results[0].ResultId, results[1].ResultId);
        await using var verification = new AppDbContext(options);
        Assert.Equal(1, await verification.PageOcrResults.CountAsync());
        Assert.Equal(1, await verification.ProcessingJobs.CountAsync(job =>
            job.Type == "RecognizePageText"));
    }

    private sealed class ConcurrencyClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
