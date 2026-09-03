using Microsoft.EntityFrameworkCore;
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
}
