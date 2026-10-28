using Application.Common.Interfaces;
using Application.Ocpp.LiveCharging;
using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.Commands.DriverSessions;

/// <summary>4.8: the driver's month at a glance — "October: 7 sessions · 142 kWh · 27.6 JOD".</summary>
public record MySessionsSummaryDto(
    /// <summary>YYYY-MM (Jordan calendar month).</summary>
    string Month,
    int Sessions,
    decimal EnergyKwh,
    decimal CostJod,
    int PointsAwarded);

/// <param name="Month">YYYY-MM; empty = the current Jordan month.</param>
public record GetMyOcppSessionsSummaryRequest(string? Month) : IRequest<MySessionsSummaryDto>;

public class GetMyOcppSessionsSummaryRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetMyOcppSessionsSummaryRequest, MySessionsSummaryDto>
{
    public async Task<MySessionsSummaryDto> Handle(GetMyOcppSessionsSummaryRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");

        int year, month;
        if (string.IsNullOrWhiteSpace(request.Month)) (year, month) = JordanMonth.Current(DateTime.UtcNow);
        else if (!JordanMonth.TryParse(request.Month, out year, out month))
            throw new DataValidationException("Month", LiveChargingTexts.MonthInvalid());

        var (from, to) = JordanMonth.Range(year, month);

        // Same set as the history list (no rejected / orphan rows), by start time. Energy and cost are the
        // final figures, so a session still running counts in "sessions" but adds its kWh / JOD once closed.
        var totals = await db.OcppTransactions.AsNoTracking()
            .Where(t => t.StartedByUserId == userId && !t.WasRejected && !t.IsOrphan && t.StartedAt >= from && t.StartedAt < to)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Sessions = g.Count(),
                Energy = g.Sum(t => t.EnergyKwh ?? 0m),
                CostFils = g.Sum(t => (long?)t.CostFils ?? 0L),
                Points = g.Sum(t => t.PointsAwarded ?? 0),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return new MySessionsSummaryDto(
            JordanMonth.Format(year, month),
            totals?.Sessions ?? 0,
            Math.Round(totals?.Energy ?? 0m, 3, MidpointRounding.AwayFromZero),
            (totals?.CostFils ?? 0L) / 1000m,
            totals?.Points ?? 0);
    }
}
