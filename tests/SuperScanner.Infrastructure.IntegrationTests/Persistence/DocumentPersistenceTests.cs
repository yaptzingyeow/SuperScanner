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
}
