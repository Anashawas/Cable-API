using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.ServiceProviders.Commands.UpdateServiceProvider;

public record UpdateServiceProviderCommand(
    int Id,
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
    bool IsVerified,
    bool HasOffer,
    string? OfferDescription,
    string? Service,
    string? Note,
    string? WhatsAppNumber,
    string? WebsiteUrl
) : IRequest;

public class UpdateServiceProviderCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<UpdateServiceProviderCommand>
{
    public async Task Handle(UpdateServiceProviderCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var serviceProvider = await applicationDbContext.ServiceProviders
                                  .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.Id, cancellationToken)
                              ?? throw new NotFoundException($"Service provider with id {request.Id} not found");

        if (serviceProvider.OwnerId != userId)
            throw new ForbiddenAccessException("You are not the owner of this service provider");

        serviceProvider.Name = request.Name;
        serviceProvider.ServiceCategoryId = request.ServiceCategoryId;
        serviceProvider.StatusId = request.StatusId;
        serviceProvider.Description = request.Description;
        serviceProvider.Phone = request.Phone;
        serviceProvider.OwnerPhone = request.OwnerPhone;
        serviceProvider.Address = request.Address;
        serviceProvider.CountryName = request.CountryName;
        serviceProvider.CityName = request.CityName;
        serviceProvider.Latitude = request.Latitude;
        serviceProvider.Longitude = request.Longitude;
        serviceProvider.Price = request.Price;
        serviceProvider.PriceDescription = request.PriceDescription;
        serviceProvider.FromTime = request.FromTime;
        serviceProvider.ToTime = request.ToTime;
        serviceProvider.MethodPayment = request.MethodPayment;
        serviceProvider.IsVerified = request.IsVerified;
        serviceProvider.HasOffer = request.HasOffer;
        serviceProvider.OfferDescription = request.OfferDescription;
        serviceProvider.Service = request.Service;
        serviceProvider.Note = request.Note;
        serviceProvider.WhatsAppNumber = request.WhatsAppNumber;
        serviceProvider.WebsiteUrl = request.WebsiteUrl;

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
