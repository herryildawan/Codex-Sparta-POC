using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;
namespace Sparta.WebApi.Telemetry;

public static class SpartaTelemetry {
    public const string Name = "Sparta.Business";
    public static readonly ActivitySource Activities = new(Name);
    public static readonly Meter Meter = new(Name);
    public static readonly Counter<long> Operations = Meter.CreateCounter<long>("sparta.operations");
}
public sealed class DatabaseHealthCheck(string connectionString) : IHealthCheck {
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) {
        try {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            using var command = connection.CreateCommand(); command.CommandText = "SELECT 1"; command.CommandTimeout = 5;
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        } catch { return HealthCheckResult.Unhealthy("Database connectivity check failed."); }
    }
}
