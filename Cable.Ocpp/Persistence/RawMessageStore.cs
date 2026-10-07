using System.Threading.Channels;
using Microsoft.Data.SqlClient;

namespace Cable.Ocpp.Persistence;

public sealed record RawMessage(
    string ChargePointId,
    string Direction,      // "in" | "out" | "sys"
    int? MessageType,
    string? MessageId,
    string? Action,
    string? Payload,
    string? RemoteIp,
    DateTime CreatedAt);

/// <summary>
/// Every frame in and out lands in dbo.OcppRawMessage. This is the one diagnostic
/// tool that matters when a real charger misbehaves, so it is written on a
/// background channel and never blocks the socket. Without a connection string
/// it degrades to the ILogger only, which keeps local runs trivial.
/// </summary>
public sealed class RawMessageStore : BackgroundService
{
    private const string InsertSql = """
        INSERT INTO dbo.OcppRawMessage (ChargePointId, Direction, MessageType, MessageId, [Action], Payload, RemoteIp, CreatedAt)
        VALUES (@cp, @dir, @type, @mid, @action, @payload, @ip, @at);
        """;

    private readonly Channel<RawMessage> _queue = Channel.CreateBounded<RawMessage>(new BoundedChannelOptions(10_000)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
    });

    private readonly string? _connectionString;
    private readonly ILogger<RawMessageStore> _log;

    public long Written { get; private set; }
    public long Failed { get; private set; }
    public bool DatabaseEnabled => !string.IsNullOrWhiteSpace(_connectionString);

    public RawMessageStore(IConfiguration configuration, ILogger<RawMessageStore> log)
    {
        _connectionString = configuration.GetConnectionString("Cable");
        _log = log;
        if (!DatabaseEnabled)
            _log.LogWarning("ConnectionStrings:Cable is empty — raw OCPP messages are logged but not stored");
    }

    public void Log(RawMessage message)
    {
        _log.LogInformation("{Direction} {ChargePointId} {Action} {Payload}",
            message.Direction.ToUpperInvariant(), message.ChargePointId, message.Action ?? "-", message.Payload);
        _queue.Writer.TryWrite(message);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!DatabaseEnabled)
        {
            // Drain so the bounded channel never fills.
            await foreach (var _ in _queue.Reader.ReadAllAsync(stoppingToken)) { }
            return;
        }

        var batch = new List<RawMessage>(100);
        while (await _queue.Reader.WaitToReadAsync(stoppingToken))
        {
            batch.Clear();
            while (batch.Count < 100 && _queue.Reader.TryRead(out var item))
                batch.Add(item);

            try
            {
                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(stoppingToken);
                await using var tx = await connection.BeginTransactionAsync(stoppingToken);
                foreach (var m in batch)
                {
                    await using var cmd = new SqlCommand(InsertSql, connection, (SqlTransaction)tx);
                    cmd.Parameters.AddWithValue("@cp", m.ChargePointId);
                    cmd.Parameters.AddWithValue("@dir", m.Direction);
                    cmd.Parameters.AddWithValue("@type", (object?)m.MessageType ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@mid", (object?)m.MessageId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@action", (object?)m.Action ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@payload", (object?)m.Payload ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ip", (object?)m.RemoteIp ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@at", m.CreatedAt);
                    await cmd.ExecuteNonQueryAsync(stoppingToken);
                }
                await tx.CommitAsync(stoppingToken);
                Written += batch.Count;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Failed += batch.Count;
                _log.LogError(ex, "Failed to store {Count} raw OCPP messages", batch.Count);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
