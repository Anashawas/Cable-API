using System.Globalization;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Commands.UpdateSettlementStatus;

public record UpdateSettlementStatusCommand(
    int Id,
    int Status,
    string? Note
) : IRequest;

public class UpdateSettlementStatusCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<UpdateSettlementStatusCommand>
{
    public async Task Handle(UpdateSettlementStatusCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var settlement = await applicationDbContext.ProviderSettlements
                             .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
                         ?? throw new NotFoundException($"Settlement with id {request.Id} not found");

        // Lock: once Paid, no further status changes allowed
        if (settlement.SettlementStatus == (int)SettlementStatus.Paid)
            throw new DataValidationException("Status",
                "This settlement is already Paid and cannot be modified");

        // Block Paid status if the settlement week has not ended yet
        if (request.Status == (int)SettlementStatus.Paid)
        {
            var (currentYear, currentWeek) = GetSundaySaturdayWeek(DateTime.UtcNow);

            if (settlement.PeriodYear == currentYear && settlement.PeriodWeek == currentWeek)
                throw new DataValidationException("Status",
                    "Cannot mark settlement as Paid while the current week is still active. Please wait until the week ends (Saturday).");
        }

        settlement.SettlementStatus = request.Status;
        settlement.AdminNote = request.Note;

        if (request.Status == (int)SettlementStatus.Paid)
        {
            settlement.PaidAt = DateTime.UtcNow;
            // WalletApplied is already accumulated per-transaction by SettlementService
            // No wallet deduction here — commission was already deducted per-transaction in real-time
        }

        await applicationDbContext.SaveChanges(cancellationToken);
    }

    /// <summary>
    /// Calculates the week number using Sunday-Saturday week boundaries.
    /// </summary>
    private static (int Year, int Week) GetSundaySaturdayWeek(DateTime date)
    {
        var calendar = CultureInfo.InvariantCulture.Calendar;
        var week = calendar.GetWeekOfYear(date, CalendarWeekRule.FirstDay, DayOfWeek.Sunday);
        return (date.Year, week);
    }
}
