namespace Application.SystemVersions.Commands.AddSystemVersion;

public record AddSystemVersionCommand(
    string Platform,
    string Version,
    string? ForceUpdate) : IRequest<int>;

public class AddSystemVersionCommandHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<AddSystemVersionCommand, int>
{
    public async Task<int> Handle(AddSystemVersionCommand request, CancellationToken cancellationToken)
    {
        var systemVersion = new SystemVersion()
        {
            Version = request.Version,
            Platform = request.Platform,
            ForceUpdate = request.ForceUpdate
        };
        applicationDbContext.SystemVersions.Add(systemVersion);
        await applicationDbContext.SaveChanges(cancellationToken);
        return systemVersion.Id;
    }
}