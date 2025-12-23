using System.Reflection;
using System.Text.Json;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Cable.Security.Jwt;
using Cable.Security.Jwt.Interfaces;
using FluentValidation;
using Hangfire;
using Infrastructrue.Firebase.FirebaseService;
using Infrastructrue.Firebase.NotificationService;
using Infrastructrue.Identity;
using infrastructrue.Options;
using Infrastructrue.Options;
using Infrastructrue.Persistence;
using Infrastructrue.Persistence.Interceptors;
using Infrastructrue.Persistence.Repositories;
using Infrastructrue.UploadFiles;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
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
        services.RegisterDbContext(configurations)
            .RegisterIdentity(configurations)
            .RegisterUploadFiles(configurations)
            .RegisterGoogleService(configurations)
            .RegisterFirebaseService(configurations)
            .RegisterHangFire(configurations)
            .RegisterSharedLink(configurations)
            .RegisterOtpServices(configurations)
            .RegisterRepositories();

        return services;
    }


    private static IServiceCollection RegisterGoogleService(this IServiceCollection services,
        IConfiguration configurations)
    {
        var googleOption = configurations.GetSection(GoogleOption.ConfigName);
        services.Configure<GoogleOption>(googleOption);
        return services;
    }

    private static IServiceCollection RegisterFirebaseService(this IServiceCollection services,
        IConfiguration configurations)
    {
        var firebaseOption = configurations.GetSection(FirebaseOption.ConfigName);
        services.Configure<FirebaseOption>(firebaseOption);
        services.AddSingleton<IFirebaseService, FirebaseService>();
        services.AddScoped<INotificationService, NotificationService>();      
        return services;
        
    }

    private static IServiceCollection RegisterSharedLink(this IServiceCollection services,
        IConfiguration configurations)
    {
        var sharedLinkOptionsSection = configurations.GetSection(SharedLinkOptions.ConfigName);
        services.Configure<SharedLinkOptions>(sharedLinkOptionsSection);
        
        return services;
    }
    private static IServiceCollection RegisterUploadFiles(this IServiceCollection services,
        IConfiguration configurations)
    {
        var uploadFileOptionsSection = configurations.GetSection(UploadFileOptions.ConfigName);
        services.Configure<UploadFileOptions>(uploadFileOptionsSection);
        services.AddScoped<IUploadFileService, UploadFileService>();
        return services;
    }

    private static IServiceCollection RegisterHangFire(this IServiceCollection services, IConfiguration configurations)
    {
        services.AddHangfire(opt =>
            {
                opt.SetDataCompatibilityLevel(CompatibilityLevel.Version_180);
                opt.UseSqlServerStorage(configurations.GetConnectionString("Cable"));
                opt.UseSimpleAssemblyNameTypeSerializer();
                opt.UseRecommendedSerializerSettings();
            }
        );
        services.AddHangfireServer();
        return services;
    }


    private static IServiceCollection RegisterOtpServices(this IServiceCollection services, IConfiguration configurations)
    {
        // Configure OTP options
        services.Configure<OtpOptions>(configurations.GetSection(OtpOptions.ConfigName));
        services.Configure<SmsOptions>(configurations.GetSection(SmsOptions.ConfigName));
        
        // Register services
        services.AddScoped<IOtpService, Services.OtpService>();
        services.AddScoped<ISmsService, Services.SmsService>();
        
        return services;
    }

    private static IServiceCollection RegisterRepositories(this IServiceCollection services)
    {
        services.AddScoped<IRateRepository, RateRepository>();
        services.AddScoped<IUserAccountRepository, UserAccountRepository>();
        services.AddScoped<IChargingPointRepository, ChargingPointRepository>();
        services.AddScoped<ISharedLinkRepository, SharedLinkRepository>();
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
        services.AddScoped<IApplicationDbContextProcedures, ApplicationDbContextProcedures>();
        services.AddScoped<SaveChangesInterceptor, AuditableEntitySaveChangesInterceptor>();


        return services;
    }
}