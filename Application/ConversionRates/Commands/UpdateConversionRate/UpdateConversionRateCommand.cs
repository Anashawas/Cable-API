using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.ConversionRates.Commands.UpdateConversionRate;

public record UpdateConversionRateCommand(
    int Id,
    string Name,
    string CurrencyCode,
    double PointsPerUnit,
    bool IsDefault,
    bool IsActive
) : IRequest;

public class UpdateConversionRateCommandHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<UpdateConversionRateCommand>
{
    public async Task Handle(UpdateConversionRateCommand request, CancellationToken cancellationToken)
    {
        var rate = await applicationDbContext.PointsConversionRates
                       .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
                   ?? throw new NotFoundException($"Conversion rate with id {request.Id} not found");

        rate.Name = request.Name;
        rate.CurrencyCode = request.CurrencyCode;
        rate.PointsPerUnit = request.PointsPerUnit;
        rate.IsDefault = request.IsDefault;
        rate.IsActive = request.IsActive;

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
