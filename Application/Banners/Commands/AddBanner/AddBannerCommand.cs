using Cable.Core.Utilities;

namespace Application.Banners.Commands.AddBanner;

public record AddBannerCommand(string Name, string Phone, string Email,int? ActionType,string? ActionUrl, DateOnly StartDate, DateOnly EndDate)
    : IRequest<int>;

public class AddBannerCommandHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<AddBannerCommand, int>
{
    public async Task<int> Handle(AddBannerCommand request, CancellationToken cancellationToken)
    {
        // Normalize phone number if provided
        var normalizedPhone = !string.IsNullOrEmpty(request.Phone) 
            ? PhoneNumberUtility.NormalizePhoneNumber(request.Phone) ?? request.Phone 
            : request.Phone;

        var banner = new Banner()
        {
            Email = request.Email,
            Name = request.Name,
            Phone = normalizedPhone,
            ActionType = request.ActionType,
            ActionUrl = request.ActionUrl,
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