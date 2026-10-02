using ArksScanner.Infrastructure.Signatures;

namespace ArksScanner.Worker;

public sealed class SignatureCleanupWorker(IServiceScopeFactory scopes, ILogger<SignatureCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<SignatureAssetCleanup>().RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception) { logger.LogError("Signature cleanup failed. ErrorCode={ErrorCode}", "signature_cleanup_failed"); }
        }
    }
}
