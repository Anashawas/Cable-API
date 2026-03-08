namespace Application.Loyalty.Commands.AwardPoints;

public record AwardPointsCommand(
    int UserId,
    string ActionCode,
    string? ReferenceType = null,
    int? ReferenceId = null,
    string? Note = null
) : IRequest<int>;

public class AwardPointsCommandHandler(
    ILoyaltyPointService loyaltyPointService)
    : IRequestHandler<AwardPointsCommand, int>
{
    public async Task<int> Handle(AwardPointsCommand request, CancellationToken cancellationToken)
    {
        return await loyaltyPointService.AwardPointsAsync(
            request.UserId,
            request.ActionCode,
            request.ReferenceType,
            request.ReferenceId,
            request.Note,
            cancellationToken);
    }
}
