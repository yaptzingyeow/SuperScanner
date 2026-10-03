using ArksScanner.Infrastructure.Processing;

namespace ArksScanner.Worker;

public sealed class DocumentRetentionWorker(IServiceScopeFactory scopes,
    ILogger<DocumentRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<DocumentRetention>()
                    .RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception) { logger.LogError("Document retention failed. ErrorCode={ErrorCode}", "document_retention_failed"); }
        }
    }
}
