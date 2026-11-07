using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Attendance.Holidays;

public record PublicHolidayDto(int Id, DateOnly Date, string NameAr, string NameEn)
{
    public string Name => Lang.Pick(NameAr, NameEn);
}

/// <summary>Any signed-in user: the holidays of a year (default: this year).</summary>
public record GetPublicHolidaysRequest(int? Year) : IRequest<List<PublicHolidayDto>>;

public class GetPublicHolidaysRequestHandler(IApplicationDbContext db) : IRequestHandler<GetPublicHolidaysRequest, List<PublicHolidayDto>>
{
    public async Task<List<PublicHolidayDto>> Handle(GetPublicHolidaysRequest request, CancellationToken cancellationToken)
    {
        var year = request.Year ?? JordanTime.FromUtc(DateTime.UtcNow).Year;
        var from = new DateOnly(year, 1, 1); var to = new DateOnly(year, 12, 31);
        return await db.PublicHolidays.AsNoTracking()
            .Where(h => !h.IsDeleted && h.Date >= from && h.Date <= to)
            .OrderBy(h => h.Date)
            .Select(h => new PublicHolidayDto(h.Id, h.Date, h.NameAr, h.NameEn))
            .ToListAsync(cancellationToken);
    }
}

public record PublicHolidayInput(DateOnly Date, string NameAr, string NameEn);

/// <summary>Admin: the full list of a year replaces what was there (missing dates are removed).</summary>
public record SetPublicHolidaysCommand(int Year, List<PublicHolidayInput> Days) : IRequest<List<PublicHolidayDto>>;

public class SetPublicHolidaysCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser) : IRequestHandler<SetPublicHolidaysCommand, List<PublicHolidayDto>>
{
    public async Task<List<PublicHolidayDto>> Handle(SetPublicHolidaysCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        if (request.Year is < 2024 or > 2100) throw new DataValidationException("Year", "Year out of range");

        var input = request.Days ?? [];
        if (input.Any(d => d.Date.Year != request.Year))
            throw new DataValidationException("Days", Lang.Pick("كل التواريخ لازم تكون بنفس السنة", "Every date must be in that year"));
        if (input.Select(d => d.Date).Distinct().Count() != input.Count)
            throw new DataValidationException("Days", Lang.Pick("تاريخ مكرّر", "Duplicate date"));
        if (input.Any(d => string.IsNullOrWhiteSpace(d.NameAr) || string.IsNullOrWhiteSpace(d.NameEn)))
            throw new DataValidationException("Days", Lang.Pick("الاسم بالعربي والإنجليزي مطلوب", "Arabic and English names are required"));

        var from = new DateOnly(request.Year, 1, 1); var to = new DateOnly(request.Year, 12, 31);
        var existing = await db.PublicHolidays.Where(h => !h.IsDeleted && h.Date >= from && h.Date <= to).ToListAsync(cancellationToken);
        foreach (var h in existing.Where(h => input.All(i => i.Date != h.Date))) h.IsDeleted = true;
        foreach (var i in input)
        {
            var h = existing.FirstOrDefault(x => x.Date == i.Date);
            if (h is null) db.PublicHolidays.Add(new PublicHoliday { Date = i.Date, NameAr = i.NameAr.Trim(), NameEn = i.NameEn.Trim() });
            else { h.NameAr = i.NameAr.Trim(); h.NameEn = i.NameEn.Trim(); }
        }
        await db.SaveChanges(cancellationToken);

        return await db.PublicHolidays.AsNoTracking().Where(h => !h.IsDeleted && h.Date >= from && h.Date <= to).OrderBy(h => h.Date)
            .Select(h => new PublicHolidayDto(h.Id, h.Date, h.NameAr, h.NameEn)).ToListAsync(cancellationToken);
    }
}
