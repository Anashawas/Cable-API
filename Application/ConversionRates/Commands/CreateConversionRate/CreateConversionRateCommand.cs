namespace Application.ConversionRates.Commands.CreateConversionRate;

public record CreateConversionRateCommand(
    string Name,
    string CurrencyCode,
    double PointsPerUnit,
    bool IsDefault,
    bool IsActive
) : IRequest<int>;

public class CreateConversionRateCommandHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<CreateConversionRateCommand, int>
{
    public async Task<int> Handle(CreateConversionRateCommand request, CancellationToken cancellationToken)
    {
        var rate = new PointsConversionRate
        {
            Name = request.Name,
            CurrencyCode = request.CurrencyCode,
            PointsPerUnit = request.PointsPerUnit,
            IsDefault = request.IsDefault,
            IsActive = request.IsActive
        };

        applicationDbContext.PointsConversionRates.Add(rate);
        await applicationDbContext.SaveChanges(cancellationToken);

        return rate.Id;
    }
}
