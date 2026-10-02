using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SuperScanner.Application.Abstractions;
using SuperScanner.Domain.Documents;
using SuperScanner.Domain.Processing;
using SuperScanner.Infrastructure.Persistence;
using SuperScanner.Infrastructure.Processing;
using SuperScanner.Worker;

namespace SuperScanner.Application.Tests.Processing;

public sealed class RepairJobRunnerTests
{
    [Fact]
    public async Task Oversized_repair_source_fails_the_preview_and_job_without_retrying()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        Guid operationId;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            var document = Document.Create(Guid.NewGuid(), "owner", "Repair", DateTimeOffset.UtcNow);
            var page = document.AddPage(Guid.NewGuid(), 20, DateTimeOffset.UtcNow);
            db.Documents.Add(document);
            var operation = PageRepairOperation.Create(Guid.NewGuid(), page.Id, null,
                "oversized.jpg", "[[0.01,0.2,0.04,0.25]]", DateTimeOffset.UtcNow);
            operationId = operation.Id;
            db.PageRepairOperations.Add(operation);
            await db.SaveChangesAsync();
        }
        var queue = new RepairQueue(operationId);
        var services = new ServiceCollection();
        services.AddScoped(_ => new AppDbContext(options));
        services.AddScoped<PageRepairProcessor>();
        services.AddSingleton<IProcessingJobQueue>(queue);
        services.AddSingleton<IObjectStore>(new OversizedStore());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        await using var provider = services.BuildServiceProvider();
        var runner = new UploadValidationJobRunner(provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<UploadValidationJobRunner>.Instance);

        Assert.True(await runner.RunOnceAsync("worker", TimeSpan.FromMinutes(2), default));

        Assert.True(queue.Failed);
        Assert.False(queue.Rescheduled);
        await using var reader = new AppDbContext(options);
        Assert.Equal("Failed", (await reader.PageRepairOperations.SingleAsync()).State);
    }

    private sealed class RepairQueue(Guid operationId) : IProcessingJobQueue
    {
        private bool leased;
        public bool Failed { get; private set; }
        public bool Rescheduled { get; private set; }
        public Task<ProcessingJobLease?> TryLeaseAsync(string worker, TimeSpan duration, CancellationToken ct)
        {
            if (leased) return Task.FromResult<ProcessingJobLease?>(null);
            leased = true;
            return Task.FromResult<ProcessingJobLease?>(new ProcessingJobLease(Guid.NewGuid(),
                "PreviewPageRepair", operationId.ToString(), 1, DateTimeOffset.UtcNow.Add(duration)));
        }
        public Task FailAsync(Guid id, string worker, string code, CancellationToken ct)
        { Failed = true; return Task.CompletedTask; }
        public Task RescheduleAsync(Guid id, string worker, string code, CancellationToken ct)
        { Rescheduled = true; return Task.CompletedTask; }
        public Task CompleteAsync(Guid id, string worker, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> HeartbeatAsync(Guid id, string worker, TimeSpan duration, CancellationToken ct) =>
            Task.FromResult(true);
        public Task EnqueueAsync(string type, string payload, string key, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class OversizedStore : IObjectStore
    {
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) =>
            Task.FromResult<Stream>(new MemoryStream(new byte[25 * 1024 * 1024 + 1]));
        public Task<Uri> CreatePutUrlAsync(PutObjectRequest request, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<StoredObjectInfo?> HeadAsync(string key, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task PromoteAsync(string source, string destination, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<ObjectCreationResult> WriteIfAbsentAsync(string key, string mediaType,
            Stream content, CancellationToken ct) => throw new NotSupportedException();
    }
}
