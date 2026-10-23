using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Application.Health.Queries;

/// <summary>For uptime monitors: the API answers and reaches its database.</summary>
public record ApiHealthDto(bool Ok, string Database, DateTime CheckedAt);

public record GetApiHealthRequest : IRequest<ApiHealthDto>;

public class GetApiHealthQueryHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetApiHealthRequest, ApiHealthDto>
{
    public async Task<ApiHealthDto> Handle(GetApiHealthRequest request, CancellationToken cancellationToken)
    {
        bool canConnect;
        try
        {
            canConnect = await applicationDbContext.Database.CanConnectAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            canConnect = false;
        }

        return new ApiHealthDto(canConnect, canConnect ? "Connected" : "Not reachable", DateTime.UtcNow);
    }
}

/// <summary>
/// For uptime monitors: SMS (OTP) can be sent — provider account active, sender name active,
/// balance above SmsService:MinBalance. Problem is the first thing to fix, null when Ok.
/// </summary>
public record SmsHealthDto(
    bool Ok,
    string Account,
    string Sender,
    decimal? Balance,
    decimal MinBalance,
    string? Problem,
    DateTime CheckedAt);

public record GetSmsHealthRequest : IRequest<SmsHealthDto>;

public class GetSmsHealthQueryHandler(ISmsService smsService, IMemoryCache cache)
    : IRequestHandler<GetSmsHealthRequest, SmsHealthDto>
{
    private const string CacheKey = "health:sms";

    public async Task<SmsHealthDto> Handle(GetSmsHealthRequest request, CancellationToken cancellationToken)
    {
        // Anonymous endpoint: answer from cache so callers cannot make us hammer the provider.
        if (cache.TryGetValue(CacheKey, out SmsHealthDto? cached) && cached is not null)
            return cached;

        var status = await smsService.CheckAccountAsync(cancellationToken);

        var problem = !status.AccountOk ? $"SMS account: {status.Account}"
            : !status.SenderOk ? $"SMS sender: {status.Sender}"
            : status.Balance is null ? "SMS balance could not be read"
            : status.Balance < status.MinBalance ? $"SMS balance low: {status.Balance} (top up below {status.MinBalance})"
            : null;

        var health = new SmsHealthDto(problem is null, status.Account, status.Sender, status.Balance,
            status.MinBalance, problem, DateTime.UtcNow);

        // A problem is re-checked sooner, so a fix shows up within a minute.
        cache.Set(CacheKey, health, problem is null ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(1));
        return health;
    }
}
