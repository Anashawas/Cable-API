using Application.Common.Security;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Banners.Commands.NormalizeBannerPhones;

/// <summary>One field of one banner and what the normalisation did with it.</summary>
public record BannerPhoneFixDto(int BannerId, string BannerName, string Field, string? Before, string? After, string Result);

public record NormalizeBannerPhonesResultDto(bool DryRun, int Checked, int Changed, int Unchanged, int Invalid, List<BannerPhoneFixDto> Rows);

/// <summary>
/// One-off clean-up (BANNER_CALL_ACTION spec §3.3): brings every banner's Phone, and the ActionUrl of
/// WhatsApp / call banners, to the stored form used by add / update. Values that cannot be dialled are
/// listed as "Invalid" for manual repair and never deleted or changed. Admin only; dryRun = report only.
/// Safe to run more than once.
/// </summary>
public record NormalizeBannerPhonesCommand(bool DryRun) : IRequest<NormalizeBannerPhonesResultDto>;

public class NormalizeBannerPhonesCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<NormalizeBannerPhonesCommand, NormalizeBannerPhonesResultDto>
{
    public async Task<NormalizeBannerPhonesResultDto> Handle(NormalizeBannerPhonesCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);

        var banners = await db.Banners.Where(b => !b.IsDeleted).OrderBy(b => b.Id).ToListAsync(cancellationToken);
        var rows = new List<BannerPhoneFixDto>();

        foreach (var b in banners)
        {
            if (!string.IsNullOrWhiteSpace(b.Phone))
                rows.Add(Fix(b.Id, b.Name, "Phone", b.Phone, v => b.Phone = v, request.DryRun));

            if (BannerRules.ActionIsPhone(b.ActionType) && !string.IsNullOrWhiteSpace(b.ActionUrl))
                rows.Add(Fix(b.Id, b.Name, "ActionUrl", b.ActionUrl, v => b.ActionUrl = v, request.DryRun));
        }

        if (!request.DryRun && rows.Any(r => r.Result == "Changed"))
            await db.SaveChanges(cancellationToken);

        return new NormalizeBannerPhonesResultDto(request.DryRun, rows.Count,
            rows.Count(r => r.Result == "Changed"), rows.Count(r => r.Result == "Unchanged"), rows.Count(r => r.Result == "Invalid"),
            rows.Where(r => r.Result != "Unchanged").ToList());
    }

    private static BannerPhoneFixDto Fix(int id, string name, string field, string before, Action<string> set, bool dryRun)
    {
        var after = DialablePhone.Normalize(before);
        if (after is null) return new(id, name, field, before, null, "Invalid");
        if (after == before) return new(id, name, field, before, after, "Unchanged");
        if (!dryRun) set(after);
        return new(id, name, field, before, after, "Changed");
    }
}
