using Application.Common.Interfaces;
using MediatR.Pipeline;
using Microsoft.Extensions.Logging;

namespace Application.Common.Behaviours;

public class LoggingBehaviour<TRequest>(
    ILogger<TRequest> logger,
    ICurrentUserService currentUserService,
    IIdentityService identityService)
    : IRequestPreProcessor<TRequest>
    where TRequest : notnull
{
    private readonly ILogger _logger = logger;

    public async Task Process(TRequest request, CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var userId = currentUserService.UserId ?? (int?)null;
        string? userName = string.Empty;

        if (userId.HasValue)
        {
            userName = await identityService.GetUserName(userId.Value);
        }

        _logger.LogInformation("Cable Request: {Name} {@UserId} {@UserName} {@Request}",
            requestName, userId, userName, request);
    }
}