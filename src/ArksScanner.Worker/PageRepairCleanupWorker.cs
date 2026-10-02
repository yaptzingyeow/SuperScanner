using ArksScanner.Infrastructure.Processing;

namespace ArksScanner.Worker;

public sealed class PageRepairCleanupWorker(IServiceScopeFactory scopes,
    ILogger<PageRepairCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<PageRepairAssetCleanup>()
                    .RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception) { logger.LogError("Page repair cleanup failed. ErrorCode={ErrorCode}", "page_repair_cleanup_failed"); }
        }
    }
}
