using Microsoft.EntityFrameworkCore;

namespace Application.Banners.Queries.GetAllBanners;

public record GetAllBannersRequest() : IRequest<List<GetAllBannersDto>>;

public class GetAllBannersQueryHandler(IApplicationDbContext applicationDbContext, IMapper mapper)
    : IRequestHandler<GetAllBannersRequest, List<GetAllBannersDto>>
{
    public async Task<List<GetAllBannersDto>> Handle(GetAllBannersRequest request, CancellationToken cancellationToken)
        => mapper.Map<List<GetAllBannersDto>>(
            await applicationDbContext.Banners.AsNoTracking()
                .Where(x => !x.IsDeleted)
                .Include(x => x.BannerAttachments)
                .Include(x => x.BannerDurations).ToListAsync(cancellationToken));
}