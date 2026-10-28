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
        if (!IsDriverSession(tx)) return;

        // 4.7: automatic points first (the session is priced by now; the job is a no-op unless Ocpp.AutoPoints.Enabled),
        // then the "finished" push as its continuation, so the push can say "+330 points". Replays get points, no push.
        var pointsJob = EnqueuePoints(tx.Id);
        if (tx.StoppedAt is not DateTime stopped || now - stopped >= FreshFor) return;
        if (pointsJob is null) { Enqueue(tx.Id, DriverSessionMessages.Completed); return; }

        var transactionId = tx.Id;
        try
        {
            jobs.ContinueJobWith<IBackgroundJobService>(pointsJob,
                s => s.NotifyOcppDriverSessionAsync(transactionId, DriverSessionMessages.Completed, CancellationToken.None),
                JobContinuationOptions.OnAnyFinishedState);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Session {TransactionId}: could not chain the driver's finished push — sending it on its own", transactionId);
            Enqueue(transactionId, DriverSessionMessages.Completed);
        }
    }

    private string? EnqueuePoints(int transactionId)
    {
        try
        {
            return jobs.Enqueue<Application.Ocpp.Points.IOcppSessionPointsService>(s => s.AwardAfterStopAsync(transactionId, CancellationToken.None));
        }
        catch (Exception ex)
        {
            // The price-ocpp-sessions job's catch-up awards it within a minute.
            log.LogWarning(ex, "Session {TransactionId}: could not enqueue the automatic points job", transactionId);
            return null;
        }
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
