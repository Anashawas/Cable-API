using Application.CarsManagement.CarsModels.Queries.GetAllCarsModels;
using Application.CarsManagement.UserCars.Queries.GetAllUserCars;
using Application.Users.Queries.GetAllUsers;
using Application.Common.Models;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;
using PlugTypeSummary = Application.ChargingPoints.Queries.GetChargingPointById.PlugTypeSummary;

namespace Application.Users.Queries.GetUserById;

public record GetUserByIdRequest(int Id) : IRequest<GetUserByIdDto>;

public class GetUserByIdQueryHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetUserByIdRequest, GetUserByIdDto>
{
    public async Task<GetUserByIdDto> Handle(GetUserByIdRequest request, CancellationToken cancellationToken)
    {
        var dto = await applicationDbContext.UserAccounts
            .Where(x => x.Id == request.Id && x.IsActive && !x.IsDeleted)
            .Select(x => new GetUserByIdDto(
                x.Id,
                x.Name,
                x.Phone,
                x.IsActive,
                x.Email,
                x.RegistrationProvider,
                x.FirebaseUId,
                x.Country,
                x.City,
                x.IsPhoneVerified,
                x.HasReadUpdateNotes,
                new RoleSummary(x.Role.Id, x.Role.Name),
                x.UserCars.GroupBy(uc => new { uc.CarModel.CarType.Id, uc.CarModel.CarType.Name })
                    .Select(carTypeGroup => new UserCarTypeDto(
                        carTypeGroup.Key.Id,
                        carTypeGroup.Key.Name,
                        carTypeGroup.GroupBy(uc => new { uc.CarModel.Id, uc.CarModel.Name })
                            .Select(carModelGroup => new UserCarModelDto(
                                carModelGroup.First().Id,
                                carModelGroup.Key.Id,
                                carModelGroup.Key.Name,
                                new UserPlugTypeDto(
                                    carModelGroup.First().PlugType.Id,
                                    carModelGroup.First().PlugType.Name,
                                    carModelGroup.First().PlugType.SerialNumber
                                )
                            )).ToList()
                    )).ToList(),
                true // HasAcceptedTerms — computed after the query (expression trees reject optional args)
            ))
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken) ?? throw new NotFoundException($"Can not find user {request.Id}");

        // Terms flag: accepted the active version for their role? (No published
        // terms at all → true, there is nothing to accept.)
        var acceptance = await applicationDbContext.UserAccounts.AsNoTracking()
            .Where(x => x.Id == request.Id)
            .Select(x => new { x.RoleId, x.AcceptedTermsVersionId })
            .FirstAsync(cancellationToken);
        var activeTermsId = await Application.Terms.TermsResolver.GetActiveIdForRoleAsync(
            applicationDbContext, acceptance.RoleId, cancellationToken);

        return dto with { HasAcceptedTerms = activeTermsId == null || acceptance.AcceptedTermsVersionId == activeTermsId };
    }
}