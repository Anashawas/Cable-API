using System.Diagnostics;
using System.Reflection;
using Microsoft.Data.SqlClient;

namespace Cable.Ocpp.Persistence;

/// <summary>
/// Records every process start in dbo.OcppProcessStart. On shared hosting we cannot
/// see app-pool recycles any other way; after a week the row count answers "how
/// often does the host actually restart us" — the number that decides shared vs VPS.
/// </summary>
public sealed class ProcessLifetime(IConfiguration configuration, IHostEnvironment environment, ILogger<ProcessLifetime> log)
    : IHostedService
{
    private readonly string? _connectionString = configuration.GetConnectionString("Cable");

    public DateTime StartedAt { get; } = DateTime.UtcNow;
    public string Version { get; } = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
    public int? StartsLast7Days { get; private set; }
    public DateTime? PreviousStartAt { get; private set; }

    public TimeSpan Uptime => DateTime.UtcNow - StartedAt;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
            return;

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using (var previous = new SqlCommand("SELECT MAX(StartedAt) FROM dbo.OcppProcessStart", connection))
            {
                var value = await previous.ExecuteScalarAsync(cancellationToken);
                PreviousStartAt = value is DateTime dt ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : null;
            }

            await using (var insert = new SqlCommand("""
                INSERT INTO dbo.OcppProcessStart (StartedAt, MachineName, ProcessId, EnvironmentName, Version)
                VALUES (@at, @machine, @pid, @env, @ver);
                """, connection))
            {
                insert.Parameters.AddWithValue("@at", StartedAt);
                insert.Parameters.AddWithValue("@machine", Environment.MachineName);
                insert.Parameters.AddWithValue("@pid", Environment.ProcessId);
                insert.Parameters.AddWithValue("@env", environment.EnvironmentName);
                insert.Parameters.AddWithValue("@ver", Version);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var count = new SqlCommand("SELECT COUNT(*) FROM dbo.OcppProcessStart WHERE StartedAt >= @since", connection))
            {
                count.Parameters.AddWithValue("@since", StartedAt.AddDays(-7));
                StartsLast7Days = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken));
            }

            log.LogInformation("Process start recorded. Starts in last 7 days: {Count}, previous start: {Previous:u}",
                StartsLast7Days, PreviousStartAt);
        }
        catch (Exception ex)
        {
            // A missing table must not stop the OCPP endpoint from serving chargers.
            log.LogError(ex, "Could not record process start (is Scripts/OcppConnect_Phase0.sql applied?)");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
