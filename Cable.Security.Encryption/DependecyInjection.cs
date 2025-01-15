using Cable.Security.Encryption;
using Cable.Security.Encryption.Interfaces;
using Cable.Security.Encryption.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension class to add default implementations
/// </summary>
public static class DependecyInjection
{

    /// <summary>
    /// Adds the default implementation for data encryption using TripleDesEncryption
    /// </summary>
    /// <param name="services">the <see cref="IServiceCollection"/> services</param>
    /// <param name="configure">A delegate which is run to configure the services.</param>
    /// <returns></returns>
    public static IServiceCollection AddTripleDesEncryption(this IServiceCollection services, Action<EncryptionOptions> configure)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configure == null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        services.Configure(configure);

        services.AddSingleton<IDataEncryption,TripleDESDataEncryption>();

        return services;
    }

    /// <summary>
    /// Adds the default implementation for Password Hasher
    /// </summary>
    /// <param name="services">the <see cref="IServiceCollection"/> services</param>
    /// <returns>An instanc eof<see cref="IServiceCollection"/></returns>
    public static IServiceCollection AddPasswordHasher(this IServiceCollection services)
    => services.AddPasswordHasher(_ => { });

    /// <summary>
    /// Adds the default implementation for Password Hasher and configure the options
    /// </summary>
    /// <param name="services">the <see cref="IServiceCollection"/> services</param>
    /// <param name="configure">A delegate which is run to configure the services.</param>
    /// <returns></returns>
    public static IServiceCollection AddPasswordHasher(this IServiceCollection services,
        Action<PasswordHasherOptions> configure)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configure == null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        services.Configure(configure);

        services.AddSingleton<IPasswordHasher, PasswordHasher>();

        return services;
    }
}