using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Banners.Commands.UpdateBanner;

public record UpdateBannerCommand(
    int Id,
    string Name,
    string Phone,
    string Email,
    int? ActionType,
    string? ActionUrl,
    DateOnly? StartDate,
    DateOnly? EndDate)
    : IRequest;

public class UpdateBannerCommandHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<UpdateBannerCommand>
{
    public async Task Handle(UpdateBannerCommand request, CancellationToken cancellationToken)
    {
        var banner =
            await applicationDbContext.Banners.FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted,
                cancellationToken)
            ?? throw new NotFoundException($"Can not find banner with id {request.Id}");

        // Normalize phone number if provided
        var normalizedPhone = !string.IsNullOrEmpty(request.Phone) 
            ? PhoneNumberUtility.NormalizePhoneNumber(request.Phone) ?? request.Phone 
            : request.Phone;

        banner.Name = request.Name;
        banner.Phone = normalizedPhone;
        banner.Email = request.Email;
        banner.ActionType = request.ActionType;
        banner.ActionUrl = request.ActionUrl;
        
        if (request.StartDate.HasValue || request.EndDate.HasValue)
        {
            var bannerDuration =
                await applicationDbContext.BannerDurations.FirstOrDefaultAsync(
                    x => x.BannerId == banner.Id && !x.IsDeleted, cancellationToken)
                ?? throw new NotFoundException($"Can not find banner duration with banner id {banner.Id}");
            bannerDuration.StartDate = request.StartDate ?? bannerDuration.StartDate;
            bannerDuration.EndDate = request.EndDate ?? bannerDuration.EndDate;
        }

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}