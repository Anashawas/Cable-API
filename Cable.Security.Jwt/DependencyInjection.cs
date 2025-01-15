using Cable.Security.Jwt.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Cable.Security.Jwt;

public static class DependencyInjection
{
    /// <summary>
    /// Adds the default implementation for Token Generation Services
    /// </summary>
    /// <param name="services">the <see cref="IServiceCollection"/> services</param>
    /// <param name="configure">A delegate which is run to configure the services.</param>
    /// <returns></returns>
    public static IServiceCollection AddTokenGeneationService(this IServiceCollection services,
        Action<TokenGeneationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        services.AddScoped<ITokenGenerationService, TokenGenerationService>();
        return services;
    }
}
