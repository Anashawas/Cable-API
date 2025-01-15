using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.PlugTypes.Commands.UpdateplugType;

public record UpdatePlugTypeCommand(int Id, string? Name, string SerialNumber) : IRequest;

public class UpdatePlugTypeCommandHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<UpdatePlugTypeCommand>
{
    public async Task Handle(UpdatePlugTypeCommand request, CancellationToken cancellationToken)
    {
        var plugType =
            await applicationDbContext.PlugTypes.FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted,
                cancellationToken)
            ?? throw new NotFoundException($"can not find plug type with id {request.Id}");
        plugType.Name = request.Name;
        plugType.SerialNumber = request.SerialNumber;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}