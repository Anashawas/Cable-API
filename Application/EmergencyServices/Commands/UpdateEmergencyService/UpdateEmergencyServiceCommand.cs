using Application.Common.Interfaces;
using Cable.Core.Exceptions;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Application.EmergencyServices.Commands.UpdateEmergencyService;

public record UpdateEmergencyServiceCommand(
    int Id,
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
) : IRequest;

public class UpdateEmergencyServiceCommandHandler(IApplicationDbContext context)
    : IRequestHandler<UpdateEmergencyServiceCommand>
{
    public async Task Handle(UpdateEmergencyServiceCommand request, CancellationToken cancellationToken)
    {
        var emergencyService = await context.EmergencyServices
            .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(EmergencyService), request.Id);

        emergencyService.Title = request.Title;
        emergencyService.Description = request.Description;
        emergencyService.ImageUrl = request.ImageUrl;
        emergencyService.SubscriptionType = request.SubscriptionType;
        emergencyService.PriceDetails = request.PriceDetails;
        emergencyService.ActionUrl = request.ActionUrl;
        emergencyService.OpenFrom = request.OpenFrom;
        emergencyService.OpenTo = request.OpenTo;
        emergencyService.PhoneNumber = request.PhoneNumber;
        emergencyService.WhatsAppNumber = request.WhatsAppNumber;
        emergencyService.IsActive = request.IsActive;
        emergencyService.SortOrder = request.SortOrder;

        await context.SaveChanges(cancellationToken);
    }
}
