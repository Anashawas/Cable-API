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
using Infrastructrue.Loyalty;
using Infrastructrue.BackgroundJobs;
using Infrastructrue.Reports.Services;
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

        // Backs the LastSeenAt write throttle in UserActivityTrackingMiddleware.
        // Deliberately in-memory and per-instance: the throttle only needs to
        // stop a burst from one app session, and a few extra writes after a
        // restart or across instances cost nothing.
        services.AddMemoryCache();

        // Register HybridCache (.NET 9 feature)
        services.AddHybridCache(options =>
        {
            options.MaximumPayloadBytes = 1024 * 1024; // 1 MB max payload
            options.MaximumKeyLength = 1024; // 1 KB max key length
            options.DefaultEntryOptions = new Microsoft.Extensions.Caching.Hybrid.HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(10), // Default expiration
                LocalCacheExpiration = TimeSpan.FromMinutes(5) // L1 cache expiration
            };
        });

        services.AddCableDatabase(configurations)
            .RegisterIdentity(configurations)
            .RegisterUploadFiles(configurations)
            .RegisterGoogleService(configurations)
            .RegisterFirebaseService(configurations)
            .RegisterHangFire(configurations)
            .RegisterSharedLink(configurations)
            .RegisterOtpServices(configurations)
            .RegisterEmailServices(configurations)
            .RegisterReportsProvider(configurations)
            .RegisterBackgroundJobServices()
            .RegisterOcppServer(configurations)
            .RegisterRepositories();

        return services;
    }


    private static IServiceCollection RegisterReportsProvider(this IServiceCollection services, IConfiguration configuration)
    {
        FastReport.Utils.RegisteredObjects.AddConnection(typeof(FastReport.Data.MsSqlDataConnection));
        services.Configure<ReportsOptions>(configuration.GetSection(ReportsOptions.ConfigName));
        services.AddScoped<IReportService, ReportService>();
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
        services.AddScoped<IReceiptPdfService, Services.ReceiptPdfService>();
        return services;
    }

    private static IServiceCollection RegisterHangFire(this IServiceCollection services, IConfiguration configurations)
    {
        services.AddCableHangfireClient(configurations);
        services.AddHangfireServer();
        return services;
    }

    /// <summary>
    /// Hangfire storage + IBackgroundJobClient without a server. Cable.Ocpp enqueues
    /// jobs (fault notifications) that the API's Hangfire server executes with the
    /// full infrastructure (Firebase, inbox) behind it.
    /// </summary>
    public static IServiceCollection AddCableHangfireClient(this IServiceCollection services, IConfiguration configurations)
    {
        services.AddHangfire(opt =>
            {
                opt.SetDataCompatibilityLevel(CompatibilityLevel.Version_180);
                opt.UseSqlServerStorage(configurations.GetConnectionString("Cable"));
                opt.UseSimpleAssemblyNameTypeSerializer();
                opt.UseRecommendedSerializerSettings();
            }
        );
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

    private static IServiceCollection RegisterEmailServices(this IServiceCollection services, IConfiguration configurations)
    {
        // Configure Email options
        services.Configure<EmailOptions>(configurations.GetSection(EmailOptions.ConfigName));

        // Register services
        services.AddScoped<IEmailService, Services.EmailService>();
        services.AddScoped<IEmailTemplateService, Services.EmailTemplateService>();

        return services;
    }

    private static IServiceCollection RegisterBackgroundJobServices(this IServiceCollection services)
    {
        services.AddScoped<IBackgroundJobService, BackgroundJobService>();
        services.AddScoped<ISettlementService, Services.SettlementService>();
        services.AddScoped<IAnalyticsService, Services.AnalyticsService>();
        return services;
    }

    /// <summary>Cable.Ocpp host for server-initiated commands (Reset, UnlockConnector, …).</summary>
    private static IServiceCollection RegisterOcppServer(this IServiceCollection services, IConfiguration configurations)
    {
        services.Configure<Ocpp.OcppServerOptions>(configurations.GetSection(Ocpp.OcppServerOptions.ConfigName));
        services.AddHttpClient(Ocpp.OcppCommandClient.HttpClientName);
        services.AddScoped<IOcppCommandClient, Ocpp.OcppCommandClient>();
        return services;
    }

    private static IServiceCollection RegisterRepositories(this IServiceCollection services)
    {
        services.AddScoped<IRateRepository, RateRepository>();
        services.AddScoped<IUserAccountRepository, UserAccountRepository>();
        services.AddScoped<IChargingPointRepository, ChargingPointRepository>();
        services.AddScoped<ISharedLinkRepository, SharedLinkRepository>();
        services.AddScoped<ILoyaltyPointService, LoyaltyPointService>();
        services.AddScoped<ILoyaltyBoostService, LoyaltyBoostService>();
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

    /// <summary>
    /// DbContext + audit interceptor only. Public so Cable.Ocpp (a separate process)
    /// can share the database without AddInfrastructure, which would also start a
    /// Hangfire server and Firebase inside it.
    /// </summary>
    public static IServiceCollection AddCableDatabase(this IServiceCollection services, IConfiguration configurations)
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