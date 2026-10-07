
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Application.Common.Security;
using Cable.Core;

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
    List<int>? PlugTypeIds,
    List<ChargerBrandCountInput>? ChargerBrands = null
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

        // Previously any signed-in account could edit any station in the country,
        // including flipping IsVerified on its own. Owner may edit operational
        // data; only an admin may touch the trust flag.
        var isAdmin = await AdminRoleGuard.IsAdminAsync(applicationDbContext, currentUserService, cancellationToken);
        if (!isAdmin && chargingPoint.OwnerId != user.Id)
            throw new ForbiddenAccessException("Only the station owner or an admin can update this charging point.");
        
        // Normalize phone numbers if provided
        var normalizedPhone = !string.IsNullOrEmpty(request.Phone) 
            ? PhoneNumberUtility.NormalizePhoneNumber(request.Phone) ?? request.Phone 
            : request.Phone;
            
        var normalizedOwnerPhone = !string.IsNullOrEmpty(request.OwnerPhone)
            ? PhoneNumberUtility.NormalizePhoneNumber(request.OwnerPhone) ?? request.OwnerPhone
            : request.OwnerPhone;

        // Brands (many-to-many with counts). When provided, the set is REPLACED
        // (same semantics as PlugTypeIds); when omitted, the junction is untouched.
        ChargingPointBrandHelper.ResolvedBrands? brands = null;
        if (request.ChargerBrands is { Count: > 0 })
            brands = await ChargingPointBrandHelper.ResolveAsync(applicationDbContext, request.ChargerBrands, cancellationToken);

        // NOTE: ownership is intentionally NOT touched here — it previously
        // reassigned the station to whoever edited it (ownership hijack).
        // Ownership changes go through ChangeOwner only.
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
        chargingPoint.ChargersCount = brands?.TotalChargers ?? request.ChargersCount;  // auto-sum when brands given
        chargingPoint.Latitude = request.Latitude;
        chargingPoint.Longitude = request.Longitude;
        chargingPoint.ChargerPointTypeId = request.ChargerPointTypeId;
        chargingPoint.StatusId = request.StatusId;
        chargingPoint.StationTypeId = request.StationTypeId;
        chargingPoint.OwnerPhone = normalizedOwnerPhone;
        // IsVerified is a non-nullable bool on the request, so a caller that omits it
        // sends false. Honouring it only for admins also protects an owner's own
        // save from silently un-verifying their station.
        if (isAdmin)
            chargingPoint.IsVerified = request.IsVerified;
        chargingPoint.HasOffer = request.HasOffer;
        chargingPoint.Service = request.Service;
        chargingPoint.OfferDescription = request.OfferDescription;
        chargingPoint.Address = request.Address;

        // Replace the brand set when one was provided
        if (brands != null)
        {
            var existingBrands = await applicationDbContext.ChargingPointChargerBrands
                .Where(x => x.ChargingPointId == chargingPoint.Id)
                .ToListAsync(cancellationToken);
            applicationDbContext.ChargingPointChargerBrands.RemoveRange(existingBrands);

            foreach (var row in brands.Rows)
            {
                row.ChargingPointId = chargingPoint.Id;
                applicationDbContext.ChargingPointChargerBrands.Add(row);
            }
        }

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