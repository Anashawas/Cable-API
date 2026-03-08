namespace Application.Offers.Commands.GenerateSettlement;

public record GenerateSettlementCommand(int Year, int Month) : IRequest<int>;

public class GenerateSettlementCommandHandler(
    IBackgroundJobService backgroundJobService)
    : IRequestHandler<GenerateSettlementCommand, int>
{
    public async Task<int> Handle(GenerateSettlementCommand request, CancellationToken cancellationToken)
    {
        return await backgroundJobService.GenerateMonthlySettlementsAsync(
            request.Year, request.Month, cancellationToken);
    }
}
