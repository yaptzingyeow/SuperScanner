using ArksScanner.Infrastructure.Processing;

namespace ArksScanner.Worker;

public sealed class DocumentPurgeWorker(IServiceScopeFactory scopes,
    ILogger<DocumentPurgeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<DocumentPurge>()
                    .RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception) { logger.LogError("Document purge failed. ErrorCode={ErrorCode}", "document_purge_failed"); }
        }
    }
}
