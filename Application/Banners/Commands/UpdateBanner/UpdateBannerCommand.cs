using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Banners.Commands.UpdateBanner;

public record UpdateBannerCommand(
    int Id,
    string Name,
    string Phone,
    string Email,
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

        banner.Name = request.Name;
        banner.Phone = request.Phone;
        banner.Email = request.Email;
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