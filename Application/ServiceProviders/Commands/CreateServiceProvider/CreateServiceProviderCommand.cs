using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.ServiceProviders.Commands.CreateServiceProvider;

public record CreateServiceProviderCommand(
    string Name,
    int ServiceCategoryId,
    int StatusId,
    string? Description,
    string? Phone,
    string? OwnerPhone,
    string? Address,
    string? CountryName,
    string? CityName,
    double Latitude,
    double Longitude,
    double? Price,
    string? PriceDescription,
    string? FromTime,
    string? ToTime,
    string? MethodPayment,
    bool HasOffer,
    string? OfferDescription,
    string? Service,
    string? Note,
    string? WhatsAppNumber,
    string? WebsiteUrl,
    int? OwnerId = null
) : IRequest<int>;

public class CreateServiceProviderCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<CreateServiceProviderCommand, int>
{
    public async Task<int> Handle(CreateServiceProviderCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var categoryExists = await applicationDbContext.ServiceCategories
            .AnyAsync(x => x.Id == request.ServiceCategoryId && !x.IsDeleted && x.IsActive, cancellationToken);

        if (!categoryExists)
            throw new NotFoundException($"Service category with id {request.ServiceCategoryId} not found");

        // Owner defaults to the caller; an explicit OwnerId lets an admin create
        // the provider on behalf of a provider user.
        var ownerId = userId;
        if (request.OwnerId.HasValue)
        {
            var owner = await applicationDbContext.UserAccounts.AsNoTracking()
                            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.OwnerId.Value, cancellationToken)
                        ?? throw new NotFoundException($"can not find user with id {request.OwnerId}");

            if (owner.RoleId != 4)
                throw new DataValidationException("OwnerId", "Owner must have Provider role");

            ownerId = owner.Id;
        }

        var serviceProvider = new ServiceProvider
        {
            Name = request.Name,
            OwnerId = ownerId,
            ServiceCategoryId = request.ServiceCategoryId,
            StatusId = request.StatusId,
            Description = request.Description,
            Phone = request.Phone,
            OwnerPhone = request.OwnerPhone,
            Address = request.Address,
            CountryName = request.CountryName,
            CityName = request.CityName,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Price = request.Price,
            PriceDescription = request.PriceDescription,
            FromTime = request.FromTime,
            ToTime = request.ToTime,
            MethodPayment = request.MethodPayment,
            VisitorsCount = 0,
            IsVerified = false,
            HasOffer = request.HasOffer,
            OfferDescription = request.OfferDescription,
            Service = request.Service,
            Note = request.Note,
            WhatsAppNumber = request.WhatsAppNumber,
            WebsiteUrl = request.WebsiteUrl
        };

        applicationDbContext.ServiceProviders.Add(serviceProvider);
        await applicationDbContext.SaveChanges(cancellationToken);

        return serviceProvider.Id;
    }
}
