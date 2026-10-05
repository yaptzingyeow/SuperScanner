using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using ArksScanner.Infrastructure.Persistence;

namespace ArksScanner.Api.Health;

/// <summary>Ready only when the database answers and every migration is applied.</summary>
public sealed class DatabaseReadinessCheck(AppDbContext db, ILogger<DatabaseReadinessCheck> logger) : IHealthCheck
{
    public const string Tag = "ready";
    private const string InsufficientPrivilege = "42501";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            if (!await db.Database.CanConnectAsync(ct))
                return HealthCheckResult.Unhealthy("database unreachable");
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).Count();
            return pending == 0
                ? HealthCheckResult.Healthy("database ready")
                : HealthCheckResult.Unhealthy($"{pending} pending migration(s)");
        }
        catch (PostgresException exception) when (exception.SqlState == InsufficientPrivilege)
        {
            // Least-privilege app roles may not read the history table: connected, migrations unverified.
            return HealthCheckResult.Degraded(
                "database connected; migration status unknown (grant SELECT on \"__EFMigrationsHistory\" to the app role)");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Readiness check could not query the database.");
            return HealthCheckResult.Unhealthy("database unreachable");
        }
    }
}
