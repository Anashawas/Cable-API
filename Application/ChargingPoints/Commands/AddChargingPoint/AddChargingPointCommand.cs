using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Commands.AddChargingPoint;

public record AddChargingPointCommand(
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
) : IRequest<int>;

public class AddChargingPointCommandHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService,
    ICurrentUserService currentUserService)
    : IRequestHandler<AddChargingPointCommand, int>
{
    public async Task<int> Handle(AddChargingPointCommand request, CancellationToken cancellationToken)
    {
        var user = await applicationDbContext.UserAccounts.AsNoTracking()
                       .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == currentUserService.UserId, cancellationToken)
                   ?? throw new NotFoundException($"can not find user with id {currentUserService.UserId}");
        
        var normalizedPhone = !string.IsNullOrEmpty(request.Phone) 
            ? PhoneNumberUtility.NormalizePhoneNumber(request.Phone) ?? request.Phone 
            : request.Phone;
            
        var normalizedOwnerPhone = !string.IsNullOrEmpty(request.OwnerPhone) 
            ? PhoneNumberUtility.NormalizePhoneNumber(request.OwnerPhone) ?? request.OwnerPhone 
            : request.OwnerPhone;

        var chargingPoint = new ChargingPoint()
        {
            Name = request.Name,
            Note = request.Note,
            CountryName = request.CountryName,
            CityName = request.CityName,
            Phone = normalizedPhone,
            MethodPayment = request.MethodPayment,
            Price = request.Price,
            FromTime = request.FromTime,
            ToTime = request.ToTime,
            ChargerSpeed = request.ChargerSpeed,
            ChargersCount = request.ChargersCount,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            VisitorsCount = 0,
            ChargerPointTypeId = request.ChargerPointTypeId,
            StatusId = request.StatusId,
            StationTypeId = request.StationTypeId,
            OwnerPhone = normalizedOwnerPhone,
            OwnerId = user.Id,
            IsDeleted = false,
            IsVerified = request.IsVerified,
            HasOffer = request.HasOffer,
            Service = request.Service,
            OfferDescription = request.OfferDescription,
            Address = request.Address,
            ChargerBrand = request.ChargerBrand,
        };
        applicationDbContext.ChargingPoints.Add(chargingPoint);
        
        if (request.PlugTypeIds?.Any() == true)
        {
            foreach (var id in request.PlugTypeIds)
            {
                applicationDbContext.ChargingPlugs.Add(new ChargingPlug() { PlugTypeId = id, ChargingPoint = chargingPoint });
            }
        }

        await applicationDbContext.SaveChanges(cancellationToken);

        return chargingPoint.Id;
    }
}