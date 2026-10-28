using Application.Common.Interfaces;
using Application.Ocpp;
using Cable.Core.Constants;
using Domain.Enitites;
using Hangfire;

namespace Cable.Ocpp.Services;

/// <summary>
/// "Charging started / finished" pushes to the driver whose session it is. Only sessions a driver started
/// (the app's virtual tag, or a card linked to their account) — a staff start is credited to the staff member,
/// who is not the driver. Sessions a charger replays after being offline are old news and get no push.
/// The push itself runs in the API's Hangfire server (NotifyOcppDriverSessionAsync), which has Firebase.
/// </summary>
public sealed class DriverSessionNotifier(IBackgroundJobClient jobs, ILogger<DriverSessionNotifier> log)
{
    /// <summary>A start or stop older than this (offline replay) is not pushed.</summary>
    internal static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(15);

    public void Started(OcppTransaction tx, DateTime now)
    {
        if (IsDriverSession(tx) && now - tx.StartedAt < FreshFor)
            Enqueue(tx.Id, DriverSessionMessages.Started);
    }

    public void Completed(OcppTransaction tx, DateTime now)
    {
        if (IsDriverSession(tx) && tx.StoppedAt is DateTime stopped && now - stopped < FreshFor)
            Enqueue(tx.Id, DriverSessionMessages.Completed);
    }

    internal static bool IsDriverSession(OcppTransaction tx) =>
        tx.StartedByUserId is not null && !tx.WasRejected && !tx.IsOrphan && tx.StartSource != OcppStartSource.Operator;

    private void Enqueue(int transactionId, string kind)
    {
        try
        {
            jobs.Enqueue<IBackgroundJobService>(s => s.NotifyOcppDriverSessionAsync(transactionId, kind, CancellationToken.None));
        }
        catch (Exception ex)
        {
            // Hangfire storage unreachable: the session itself is saved; only the push is lost.
            log.LogWarning(ex, "Session {TransactionId}: could not enqueue the driver's '{Kind}' push", transactionId, kind);
        }
    }
}
