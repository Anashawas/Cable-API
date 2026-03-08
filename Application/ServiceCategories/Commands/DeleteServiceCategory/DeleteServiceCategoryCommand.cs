using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.ServiceCategories.Commands.DeleteServiceCategory;

public record DeleteServiceCategoryCommand(int Id) : IRequest;

public class DeleteServiceCategoryCommandHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<DeleteServiceCategoryCommand>
{
    public async Task Handle(DeleteServiceCategoryCommand request, CancellationToken cancellationToken)
    {
        var serviceCategory = await applicationDbContext.ServiceCategories
                                  .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
                              ?? throw new NotFoundException($"Service category not found with id {request.Id}");

        serviceCategory.IsDeleted = true;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
