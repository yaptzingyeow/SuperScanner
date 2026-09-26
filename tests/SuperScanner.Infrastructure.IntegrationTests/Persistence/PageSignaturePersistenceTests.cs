using Microsoft.EntityFrameworkCore;
using SuperScanner.Domain.Documents;
using SuperScanner.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SuperScanner.Infrastructure.IntegrationTests.Persistence;

public sealed class PageSignaturePersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    public Task InitializeAsync() => postgres.StartAsync();
    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Round_trip_preserves_box_aspect_ratio_and_revision()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.GetConnectionString()).Options;
        var now = DateTimeOffset.UtcNow;
        var document = Document.Create(Guid.NewGuid(), "owner", "Signature", now);
        var page = document.AddPage(Guid.NewGuid(), 50, now);
        var signature = PageSignature.Create(Guid.NewGuid(), document.Id, page.Id, Guid.NewGuid(),
            "signatures/test.png", 2, new SignatureBox(.1, .2, .3, .1), now);
        signature.MoveResize(new SignatureBox(.2, .3, .3, .1), 0, now);
        await using (var writer = new AppDbContext(options))
        {
            await writer.Database.MigrateAsync();
            writer.Documents.Add(document);
            writer.PageSignatures.Add(signature);
            await writer.SaveChangesAsync();
        }
        await using var reader = new AppDbContext(options);
        var saved = await reader.PageSignatures.SingleAsync();
        Assert.Equal(new SignatureBox(.2, .3, .3, .1), saved.Box);
        Assert.Equal(2, saved.ImageAspectRatio);
        Assert.Equal(1, saved.Revision);
        saved.Delete(1, now);
        await reader.SaveChangesAsync();
        Assert.Empty(await new EfPageSignatureRepository(reader).GetActiveForDocumentAsync(document.Id, default));
    }
}
