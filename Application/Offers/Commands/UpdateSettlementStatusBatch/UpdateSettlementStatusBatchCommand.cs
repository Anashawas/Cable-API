using System.Globalization;
using Cable.Core;
using Cable.Core.Emuns;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Commands.UpdateSettlementStatusBatch;

public record BatchStatusFailureDto(int SettlementId, string Reason);

public record UpdateSettlementStatusBatchResult(
    List<int> Updated,
    List<BatchStatusFailureDto> Failed
);

/// <summary>
/// C3 — marks multiple settlements Paid/Disputed/Pending in one call. Each id is
/// validated with the SAME rules as the single UpdateSettlementStatus (Paid is
/// permanent; can't mark Paid while the week is still active); failures are
/// reported per id instead of failing the whole batch.
/// </summary>
public record UpdateSettlementStatusBatchCommand(
    List<int> SettlementIds,
    int Status,
    string? Note
) : IRequest<UpdateSettlementStatusBatchResult>;

public class UpdateSettlementStatusBatchCommandValidator : AbstractValidator<UpdateSettlementStatusBatchCommand>
{
    public UpdateSettlementStatusBatchCommandValidator()
    {
        RuleFor(x => x.SettlementIds).NotEmpty().Must(x => x.Count <= 500)
            .WithMessage("Provide 1–500 settlement ids");
        RuleFor(x => x.Status)
            .Must(s => s is (int)SettlementStatus.Pending or (int)SettlementStatus.Paid or (int)SettlementStatus.Disputed)
            .WithMessage("Status must be 1 (Pending), 3 (Paid) or 4 (Disputed)");
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

public class UpdateSettlementStatusBatchCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<UpdateSettlementStatusBatchCommand, UpdateSettlementStatusBatchResult>
{
    public async Task<UpdateSettlementStatusBatchResult> Handle(UpdateSettlementStatusBatchCommand request,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var ids = request.SettlementIds.Distinct().ToList();
        var settlements = await applicationDbContext.ProviderSettlements
            .Where(x => ids.Contains(x.Id) && !x.IsDeleted)
            .ToListAsync(cancellationToken);

        var found = settlements.ToDictionary(x => x.Id);
        var updated = new List<int>();
        var failed = new List<BatchStatusFailureDto>();
        var now = DateTime.UtcNow;
        var (currentYear, currentWeek) = GetSundaySaturdayWeek(now);

        foreach (var id in ids)
        {
            if (!found.TryGetValue(id, out var settlement))
            {
                failed.Add(new BatchStatusFailureDto(id, "Settlement not found"));
                continue;
            }

            if (settlement.SettlementStatus == (int)SettlementStatus.Paid)
            {
                failed.Add(new BatchStatusFailureDto(id, "Already Paid — locked"));
                continue;
            }

            if (request.Status == (int)SettlementStatus.Paid
                && settlement.PeriodYear == currentYear && settlement.PeriodWeek == currentWeek)
            {
                failed.Add(new BatchStatusFailureDto(id, "Week still active — cannot mark Paid yet"));
                continue;
            }

            settlement.SettlementStatus = request.Status;
            settlement.AdminNote = request.Note;
            if (request.Status == (int)SettlementStatus.Paid)
                settlement.PaidAt = now;

            updated.Add(id);
        }

        if (updated.Count > 0)
            await applicationDbContext.SaveChanges(cancellationToken);

        return new UpdateSettlementStatusBatchResult(updated, failed);
    }

    private static (int Year, int Week) GetSundaySaturdayWeek(DateTime date)
    {
        var calendar = CultureInfo.InvariantCulture.Calendar;
        var week = calendar.GetWeekOfYear(date, CalendarWeekRule.FirstDay, DayOfWeek.Sunday);
        return (date.Year, week);
    }
}
