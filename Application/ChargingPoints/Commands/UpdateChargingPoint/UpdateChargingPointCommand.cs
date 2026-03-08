
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Commands.UpdateChargingPoint;

public record UpdateChargingPointCommand(
    int Id,
    string Name,
    string? Note,
    string? CountryName,
    string? CityName,
    string? Phone,
    string? MethodPayment,
    double? Price,
    string? FromTime,
    string? ToTime,
    int? ChargerSpeed,
    int? ChargersCount,
    double Latitude,
    double Longitude,
    int ChargerPointTypeId,
    int StatusId,
    int StationTypeId,
    string? OwnerPhone,
    bool IsVerified,
    bool HasOffer,
    string? Service,
    string? OfferDescription,
    string? Address,
    string? ChargerBrand,
    List<int>? PlugTypeIds
) : IRequest;

public class UpdateChargingPointCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService) : IRequestHandler<UpdateChargingPointCommand>
{
    public async Task Handle(UpdateChargingPointCommand request, CancellationToken cancellationToken)
    {
        var user = await applicationDbContext.UserAccounts.AsNoTracking()
                       .FirstOrDefaultAsync(x => x.Id == currentUserService.UserId, cancellationToken)
                   ?? throw new NotFoundException($"can not find user with id {currentUserService.UserId}");

        var chargingPoint = await applicationDbContext.ChargingPoints
                                .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.Id, cancellationToken)
                            ?? throw new NotFoundException($"can not find charging point with id {request.Id}");
        
        // Normalize phone numbers if provided
        var normalizedPhone = !string.IsNullOrEmpty(request.Phone) 
            ? PhoneNumberUtility.NormalizePhoneNumber(request.Phone) ?? request.Phone 
            : request.Phone;
            
        var normalizedOwnerPhone = !string.IsNullOrEmpty(request.OwnerPhone) 
            ? PhoneNumberUtility.NormalizePhoneNumber(request.OwnerPhone) ?? request.OwnerPhone 
            : request.OwnerPhone;
        
        chargingPoint.OwnerId = user.Id;
        chargingPoint.Name = request.Name;
        chargingPoint.Note = request.Note;
        chargingPoint.CountryName = request.CountryName;
        chargingPoint.CityName = request.CityName;
        chargingPoint.Phone = normalizedPhone;
        chargingPoint.MethodPayment = request.MethodPayment;
        chargingPoint.Price = request.Price;
        chargingPoint.FromTime = request.FromTime;
        chargingPoint.ToTime = request.ToTime;
        chargingPoint.ChargerSpeed = request.ChargerSpeed;
        chargingPoint.ChargersCount = request.ChargersCount;
        chargingPoint.Latitude = request.Latitude;
        chargingPoint.Longitude = request.Longitude;
        chargingPoint.ChargerPointTypeId = request.ChargerPointTypeId;
        chargingPoint.StatusId = request.StatusId;
        chargingPoint.StationTypeId = request.StationTypeId;
        chargingPoint.OwnerPhone = normalizedOwnerPhone;
        chargingPoint.IsVerified = request.IsVerified;
        chargingPoint.HasOffer = request.HasOffer;
        chargingPoint.Service = request.Service;
        chargingPoint.OfferDescription = request.OfferDescription;
        chargingPoint.Address = request.Address;
        chargingPoint.ChargerBrand = request.ChargerBrand;


        var existingChargingPlugs = await applicationDbContext.ChargingPlugs
            .Where(x => x.ChargingPointId == chargingPoint.Id && !x.IsDeleted)
            .ToListAsync(cancellationToken);

        applicationDbContext.ChargingPlugs.RemoveRange(existingChargingPlugs);
        

        if (request.PlugTypeIds?.Any() == true)
        {
            var newChargingPlugs = request.PlugTypeIds.Select(plugTypeId => new ChargingPlug 
            { 
                PlugTypeId = plugTypeId, 
                ChargingPointId = chargingPoint.Id,
                IsDeleted = false
            }).ToList();
            
            await applicationDbContext.ChargingPlugs.AddRangeAsync(newChargingPlugs, cancellationToken);
        }
        
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}