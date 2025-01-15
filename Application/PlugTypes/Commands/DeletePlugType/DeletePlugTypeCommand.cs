using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.PlugTypes.Commands.DeletePlugType;

public record DeletePlugTypeCommand(int Id) : IRequest;

public class DeletePlugTypeCommandHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<DeletePlugTypeCommand>
{
    public async Task Handle(DeletePlugTypeCommand request, CancellationToken cancellationToken)
    {
        var plugType = await applicationDbContext.PlugTypes
                           .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == request.Id, cancellationToken)
                       ?? throw new NotFoundException($"can not find plug type with id {request.Id}");

        plugType.IsDeleted = true;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}