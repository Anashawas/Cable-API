namespace Application.Banners.Commands.AddBanner;

public record AddBannerCommand(string Name, string Phone, string Email, DateOnly StartDate, DateOnly EndDate)
    : IRequest<int>;

public class AddBannerCommandHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<AddBannerCommand, int>
{
    public async Task<int> Handle(AddBannerCommand request, CancellationToken cancellationToken)
    {
        var banner = new Banner()
        {
            Email = request.Email,
            Name = request.Name,
            Phone = request.Phone,
        };
        applicationDbContext.Banners.Add(banner);
        var bannerDuration = new BannerDuration()
        {
            Banner =banner,
            StartDate = request.StartDate,
            EndDate = request.EndDate
        };
        
        applicationDbContext.BannerDurations.Add(bannerDuration);
        await applicationDbContext.SaveChanges(cancellationToken);
        return banner.Id;
    }
}