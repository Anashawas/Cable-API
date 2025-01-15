using Cable.Core.Exceptions;
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
    int StatusId
) : IRequest<int>;

public class AddChargingPointCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<AddChargingPointCommand, int>
{
    public async Task<int> Handle(AddChargingPointCommand request, CancellationToken cancellationToken)
    {
        var user = await applicationDbContext.UserAccounts.AsNoTracking()
                       .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == currentUserService.UserId, cancellationToken)
                   ?? throw new NotFoundException($"can not find user with id {currentUserService.UserId}");
        
        var chargingPoint = new ChargingPoint()
        {
            Name = request.Name,
            Note = request.Note,
            CountryName = request.CountryName,
            CityName = request.CityName,
            Phone = request.Phone,
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
            OwnerId = user.Id,
            IsDeleted = false
        };

        applicationDbContext.ChargingPoints.Add(chargingPoint);
        await applicationDbContext.SaveChanges(cancellationToken);

        return chargingPoint.Id;
    }
}