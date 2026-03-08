using Application.Common.Interfaces;
using Domain.Enitites;

namespace Application.EmergencyServices.Commands.AddEmergencyService;

public record AddEmergencyServiceCommand(
    string Title,
    string? Description,
    string? ImageUrl,
    int SubscriptionType,
    string? PriceDetails,
    string? ActionUrl,
    TimeSpan? OpenFrom,
    TimeSpan? OpenTo,
    string? PhoneNumber,
    string? WhatsAppNumber,
    bool IsActive,
    int SortOrder
) : IRequest<int>;

public class AddEmergencyServiceCommandHandler(IApplicationDbContext context)
    : IRequestHandler<AddEmergencyServiceCommand, int>
{
    public async Task<int> Handle(AddEmergencyServiceCommand request, CancellationToken cancellationToken)
    {
        var emergencyService = new EmergencyService
        {
            Title = request.Title,
            Description = request.Description,
            ImageUrl = request.ImageUrl,
            SubscriptionType = request.SubscriptionType,
            PriceDetails = request.PriceDetails,
            ActionUrl = request.ActionUrl,
            OpenFrom = request.OpenFrom,
            OpenTo = request.OpenTo,
            PhoneNumber = request.PhoneNumber,
            WhatsAppNumber = request.WhatsAppNumber,
            IsActive = request.IsActive,
            SortOrder = request.SortOrder
        };

        context.EmergencyServices.Add(emergencyService);
        await context.SaveChanges(cancellationToken);

        return emergencyService.Id;
    }
}
