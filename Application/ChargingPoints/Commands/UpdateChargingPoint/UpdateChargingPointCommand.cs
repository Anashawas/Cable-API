using Cable.Core.Exceptions;
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
    int ChargerPointTypeId
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

        chargingPoint.OwnerId = user.Id;
        chargingPoint.Name = request.Name;
        chargingPoint.Note = request.Note;
        chargingPoint.CountryName = request.CountryName;
        chargingPoint.CityName = request.CityName;
        chargingPoint.Phone = request.Phone;
        chargingPoint.MethodPayment = request.MethodPayment;
        chargingPoint.Price = request.Price;
        chargingPoint.FromTime = request.FromTime;
        chargingPoint.ToTime = request.ToTime;
        chargingPoint.ChargerSpeed = request.ChargerSpeed;
        chargingPoint.ChargersCount = request.ChargersCount;
        chargingPoint.ChargerPointTypeId = request.ChargerPointTypeId;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}