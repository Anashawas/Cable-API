using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.ServiceCategories.Commands.UpdateServiceCategory;

public record UpdateServiceCategoryCommand(
    int Id,
    string Name,
    string? NameAr,
    string? Description,
    string? IconUrl,
    int SortOrder,
    bool IsActive
) : IRequest;

public class UpdateServiceCategoryCommandHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<UpdateServiceCategoryCommand>
{
    public async Task Handle(UpdateServiceCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await applicationDbContext.ServiceCategories
                           .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
                       ?? throw new NotFoundException($"Service category with id {request.Id} not found");

        category.Name = request.Name;
        category.NameAr = request.NameAr;
        category.Description = request.Description;
        category.IconUrl = request.IconUrl;
        category.SortOrder = request.SortOrder;
        category.IsActive = request.IsActive;

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
