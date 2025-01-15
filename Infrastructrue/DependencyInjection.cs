using System.Reflection;
using System.Text.Json;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Cable.Security.Jwt;
using Cable.Security.Jwt.Interfaces;
using FluentValidation;
using Infrastructrue.Identity;
using Infrastructrue.Options;
using Infrastructrue.Persistence;
using Infrastructrue.Persistence.Interceptors;
using Infrastructrue.Persistence.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Infrastructrue;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configurations)
    {
        services.AddSingleton<JsonSerializerOptions>(new JsonSerializerOptions()
        {
            AllowTrailingCommas = true,
        });

        services.AddHttpClient();
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        services
            .RegisterDbContext(configurations)
            .RegisterIdentity(configurations)
            .RegisterRepositories();

        return services;
    }

    private static IServiceCollection RegisterRepositories(this IServiceCollection services)
    {
        services.AddScoped<IRateRepository,RateRepository >();

        return services;
    }
    
    private static IServiceCollection RegisterIdentity(this IServiceCollection services, IConfiguration configurations)
    {
        services.AddPasswordHasher();
        services.AddTripleDesEncryption(op => op.Key = "Cable APIs @2025");

        services.AddScoped<ITokenGenerationService, TokenGenerationService>();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        var tokenConfigSection = configurations.GetSection(TokenOptions.ConfigName);
        services.Configure<TokenOptions>(tokenConfigSection);
        var tokenSettings = tokenConfigSection.Get<TokenOptions>();
        var tokenValidationParameters = new TokenValidationParameters()
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            RequireExpirationTime = true,
            IssuerSigningKey =
                new SymmetricSecurityKey(Convert.FromBase64String(
                    "8ce2219efbd895bad60aa6940825d8139d924ed9bd32c23f6485529de44e52057e9ffaaad3ba96abba01f5c6f9ce7448ff039e5918e0bf29bd7162e20ac1f165")),
            ClockSkew = TimeSpan.Zero
        };
        services.AddTokenGeneationService(op =>
        {
            op.TokenValidationParameters = tokenValidationParameters;
            op.ExpiresAfter = tokenSettings.AccessTokenExpiresAfter;
        });
        services.AddAuthentication(op =>
        {
            op.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            op.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        }).AddJwtBearer(op =>
        {
            op.RequireHttpsMetadata = true;
            op.SaveToken = true;
            op.TokenValidationParameters = tokenValidationParameters;
        });

        services.AddAuthorization();

        return services;
    }

    private static IServiceCollection RegisterDbContext(this IServiceCollection services, IConfiguration configurations)
    {
        var databaseSettingsSection = configurations.GetSection(DatabaseOptions.ConfigName);
        services.Configure<DatabaseOptions>(databaseSettingsSection);
        var databaseSettings = databaseSettingsSection.Get<DatabaseOptions>();

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.EnableDetailedErrors(databaseSettings.EnableDetailedErrors);
            options.EnableSensitiveDataLogging(databaseSettings.EnableSensitiveDataLogging);

            options.UseSqlServer(configurations.GetConnectionString("Cable"), sqlOptions =>
            {
                sqlOptions.CommandTimeout(databaseSettings.CommandTimeOutInSeconds);
                sqlOptions.UseNetTopologySuite();
            });
        });

        services.AddScoped<IApplicationDbContext, ApplicationDbContext>();
        services.AddScoped<SaveChangesInterceptor, AuditableEntitySaveChangesInterceptor>();


        return services;
    }
}