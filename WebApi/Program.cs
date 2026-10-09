using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application;
using Application.Common.Interfaces;
using Cable.Core.Emuns;
using Cable.Identity;
using Cable.Routes;
using Cable.Middlewares;
using WebApi.Converters;
using Cable.WebApi.Middlewares;
using Cable.WebApi.OpenAPI.Filters;
using FluentValidation;
using Hangfire;
using Infrastructrue;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.FileProviders;
using Scalar.AspNetCore;
using static System.Enum;

string[] supportedCultures = ["en-us", "en", "ar-kw", "ar"];
const string openApiPath = "/OpenApi/Cable-API/v1.json";
var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ConfigureHttpsDefaults(httpsOptions =>
    {
        httpsOptions.SslProtocols = System.Security.Authentication.SslProtocols.Tls12 |
                                    System.Security.Authentication.SslProtocols.Tls13;
    });
    serverOptions.ConfigureEndpointDefaults(listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2AndHttp3;
    });
});
builder.Logging.AddConsole();
#pragma warning disable CA1416
builder.Logging.AddEventLog(op =>
{
    op.SourceName = "Cable-Server";
    op.LogName = "Cable";
});
#pragma warning restore CA1416


builder.Services.AddLocalization();
builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddOperationTransformer<AddAcceptLanguageHeaderParameter>();
    options.AddOperationTransformer<AuthorizationRequirementsOperationTransformer>();
    options.AddSchemaTransformer<NullableValueSchemaTransformer>();
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddApplication().AddInfrastructure(builder.Configuration);
builder.Services.AddCors(op =>
{
    var allowedHosts = builder.Configuration.GetValue<string>("AllowedHosts");
    op.AddDefaultPolicy(corsPolicyBuilder =>
    {
        if (string.IsNullOrEmpty(allowedHosts) || allowedHosts == "*")
        {
            corsPolicyBuilder.AllowAnyOrigin();
        }
        else
        {
            corsPolicyBuilder.WithOrigins(allowedHosts.Split(","));
        }

        corsPolicyBuilder.AllowAnyMethod().AllowAnyHeader();
    });
});
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(op =>
{
    ConfigureJsonSerliaizer(op.SerializerOptions);
});

builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

var app = builder.Build();


var fileUploadPath = builder.Configuration.GetValue<string>("File:FileUploadPath");

//foreach (var folder in GetNames<UploadFileFolders>())
//{
//    var fullPath = Path.Combine(fileUploadPath!, folder);
//    if (!Directory.Exists(fullPath))
//        Directory.CreateDirectory(fullPath);
//}

app.UseRequestLocalization(op =>
{
    op.SetDefaultCulture(supportedCultures[3]);
    op.AddSupportedUICultures(supportedCultures);
    op.ApplyCurrentCultureToResponseHeaders = true;
});

app.UseCableExceptionHandlerMiddleware();

// Landing page (static export in wwwroot) served at the domain root;
// API routes always win. See LandingPageMiddleware.
app.UseLandingPage();

// Cable Admin SPA served at /admin from its own wwwroot-admin folder —
// physically separate from the landing site. See AdminSpaMiddleware.
app.UseAdminSpa();

// Cable Partner SPA served at /partner from its own wwwroot-partner folder.
// See PartnerSpaMiddleware.
app.UsePartnerSpa();

// Explicit routing AFTER the landing static files. Without this, WebApplication
// auto-inserts routing at the very start of the pipeline, the MapFallback
// endpoint gets matched before the landing rewrite runs, and StaticFiles steps
// aside for requests that already have an endpoint (so /ar/ would 404).
app.UseRouting();

if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
{
    app.MapOpenApi(openApiPath);
    app.MapScalarApiReference("Cable-API/v1", opt =>
    {
        opt.WithOpenApiRoutePattern(openApiPath);
        opt.AddPreferredSecuritySchemes(JwtBearerDefaults.AuthenticationScheme);

        opt.Theme = ScalarTheme.BluePlanet;
        opt.DefaultHttpClient = new KeyValuePair<ScalarTarget, ScalarClient>(ScalarTarget.Dart, ScalarClient.Http);
        opt.ShowSidebar = true;
        opt.Title = "Cable API";
        opt.WithDocumentDownloadType(DocumentDownloadType.Both);
    });
}
if (app.Environment.IsProduction())
{
    app.MapOpenApi(openApiPath);
    app.MapScalarApiReference("Cable-API-Production/v1", opt =>
    {
        opt.WithOpenApiRoutePattern(openApiPath);
        opt.AddPreferredSecuritySchemes(JwtBearerDefaults.AuthenticationScheme);

        opt.Theme = ScalarTheme.BluePlanet;
        opt.DefaultHttpClient = new KeyValuePair<ScalarTarget, ScalarClient>(ScalarTarget.Dart, ScalarClient.Http);
        opt.ShowSidebar = true;
        opt.Title = "Cable API";
        opt.WithDocumentDownloadType(DocumentDownloadType.Both);
    });

}

app.UseCors();

// app.UseHttpsRedirection();

app.UseCustomAuthenticationResponse();
app.UseAuthentication();
app.UseAccountSessionValidation();
app.UseAuthorization();
app.UseUserActivityTracking();
app.UseSecureFileServing();
app.MapHangfireDashboard("/Cable-Jobs-Dashboard");

// ==========================================
// Register Hangfire Recurring Jobs
// ==========================================
// Cable Connect alerts: charger offline / plug Faulted / session open too long → push + inbox.
// Price alerts: a push before a tariff window starts, for users who asked (every 5 min).
RecurringJob.AddOrUpdate<IBackgroundJobService>(
    "send-price-alerts",
    service => service.SendPriceAlertsAsync(CancellationToken.None),
    "*/5 * * * *");

// Cable Connect reliability (N-6): 30-day score per charger, daily at 03:30 UTC.
RecurringJob.AddOrUpdate<IBackgroundJobService>(
    "compute-ocpp-reliability",
    service => service.ComputeOcppReliabilityAsync(CancellationToken.None),
    "30 3 * * *");

// Cable Connect: price closed sessions that the stop handler could not price (catch-up).
RecurringJob.AddOrUpdate<IBackgroundJobService>(
    "price-ocpp-sessions",
    service => service.PriceOcppSessionsAsync(CancellationToken.None),
    "*/10 * * * *");

RecurringJob.AddOrUpdate<IBackgroundJobService>(
    "check-ocpp-alerts",
    service => service.CheckOcppAlertsAsync(CancellationToken.None),
    "*/5 * * * *");

RecurringJob.AddOrUpdate<IBackgroundJobService>(
    "expire-offer-transaction-codes",
    service => service.ExpireOfferTransactionCodesAsync(CancellationToken.None),
    "*/5 * * * *");

RecurringJob.AddOrUpdate<IBackgroundJobService>(
    "expire-partner-transaction-codes",
    service => service.ExpirePartnerTransactionCodesAsync(CancellationToken.None),
    "*/5 * * * *");

// Critical: Security Cleanup (daily at 02:00 UTC)
RecurringJob.AddOrUpdate<IBackgroundJobService>(
    "cleanup-expired-phone-verifications",
    service => service.CleanupExpiredPhoneVerificationsAsync(CancellationToken.None),
    "0 2 * * *");

RecurringJob.AddOrUpdate<IBackgroundJobService>(
    "cleanup-expired-password-resets",
    service => service.CleanupExpiredPasswordResetsAsync(CancellationToken.None),
    "0 2 * * *");

RecurringJob.AddOrUpdate<IBackgroundJobService>(
    "cleanup-expired-otp-rate-limits",
    service => service.CleanupExpiredOtpRateLimitsAsync(CancellationToken.None),
    "0 2 * * *");

// Important: Business Expiry
RecurringJob.AddOrUpdate<IBackgroundJobService>(
    "deactivate-expired-offers",
    service => service.DeactivateExpiredOffersAsync(CancellationToken.None),
    "*/30 * * * *"); // Every 30 minutes

RecurringJob.AddOrUpdate<IBackgroundJobService>(
    "deactivate-expired-shared-links",
    service => service.DeactivateExpiredSharedLinksAsync(CancellationToken.None),
    "*/30 * * * *"); // Every 30 minutes

RecurringJob.AddOrUpdate<IBackgroundJobService>(
    "end-expired-loyalty-seasons",
    service => service.EndExpiredLoyaltySeasonsAsync(CancellationToken.None),
    "0 0 * * *"); // Daily at midnight UTC

RecurringJob.AddOrUpdate<IBackgroundJobService>(
    "expire-loyalty-points",
    service => service.ExpireLoyaltyPointsAsync(CancellationToken.None),
    "0 1 * * *"); // Daily at 01:00 UTC

RecurringJob.AddOrUpdate<IBackgroundJobService>(
    "deactivate-expired-rewards",
    service => service.DeactivateExpiredRewardsAsync(CancellationToken.None),
    "0 0 * * *"); // Daily at midnight UTC

RecurringJob.AddOrUpdate<IBackgroundJobService>(
    "unblock-expired-loyalty-blocks",
    service => service.UnblockExpiredLoyaltyBlocksAsync(CancellationToken.None),
    "0 */4 * * *"); // Every 4 hours

RecurringJob.AddOrUpdate<IBackgroundJobService>(
    "unblock-expired-provider-loyalty-blocks",
    service => service.UnblockExpiredProviderLoyaltyBlocksAsync(CancellationToken.None),
    "0 */4 * * *"); // Every 4 hours

// Subscriptions: switch off AfterDays-grace subscriptions whose window has
// passed. Manual-grace ones (the default) are never touched by this job.
RecurringJob.AddOrUpdate<IBackgroundJobService>(
    "apply-subscription-grace",
    service => service.ApplySubscriptionGraceAsync(CancellationToken.None),
    "30 0 * * *"); // Daily at 00:30 UTC (03:30 Amman)

// Cable Connect (OCPP): session hygiene + raw-log retention. Fault notifications are
// enqueued by Cable.Ocpp and executed here (NotifyOcppFaultAsync).
RecurringJob.AddOrUpdate<IBackgroundJobService>(
    "mark-stale-ocpp-transactions",
    service => service.MarkStaleOcppTransactionsAsync(CancellationToken.None),
    "15 * * * *"); // Hourly

RecurringJob.AddOrUpdate<IBackgroundJobService>(
    "purge-ocpp-raw-messages",
    service => service.PurgeOcppRawMessagesAsync(CancellationToken.None),
    "30 2 * * *"); // Daily at 02:30 UTC

app.MapUserRoutes()
    .MapChargingPointsRoutes()
    .MapChargingPointTypesRoutes()
    .MapPlugTypesRoutes()
    .MapRateRoutes()
    .MapStatusRoutes()
    .MapUserComplaintsRoutes()
    .MapChargingPointAttachmentsRoutes()
    .MapBannerRoutes()
    .MapBannerAttachmentRoutes()
    .MapSystemVersionRoutes()
    .MapCarManagementRoutes()
    .MapNotificationTokenRoutes()
    .MapFileRoutes()
    .MapFavoritesRoutes()
    .MapSharedLinksRoutes()
    .MapSocialMediaPlatformRoutes()
    .MapSocialLinkRoutes()
    .MapWorkerRoutes()
    .MapNotificationInboxRoutes()
    .MapNotificationTypeRoutes()
    .MapNotificationTemplateRoutes()
    .MapEmergencyServiceRoutes()
    .MapEmergencyServiceAttachmentsRoutes()
    .MapReportRoutes()
    .MapServiceProviderRoutes()
    .MapServiceCategoryRoutes()
    .MapOfferRoutes()
    .MapOfferAttachmentRoutes()
    .MapConversionRateRoutes()
    .MapSettingsRoutes()
    .MapHomeRoutes()
    .MapAdsRoutes()
    .MapAdminRoutes()
    .MapTermsRoutes()
    .MapLoyaltyRoutes()
    .MapPartnerRoutes()
    .MapProviderRoutes()
    .MapAnalyticsRoutes()
    .MapChargerBrandRoutes()
    .MapSubscriptionRoutes()
    .MapOcppRoutes()
    .MapPricingRoutes();

// Unknown non-API URLs get the landing 404 page; API-ish paths keep JSON-style 404s.
app.MapLandingPageFallback();

app.Run();

void ConfigureJsonSerliaizer(JsonSerializerOptions jsonSerializer)
{
    jsonSerializer.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    jsonSerializer.NumberHandling = JsonNumberHandling.AllowReadingFromString;
    jsonSerializer.ReadCommentHandling = JsonCommentHandling.Skip;
    jsonSerializer.AllowTrailingCommas = true;
    jsonSerializer.Converters.Add(new JsonStringEnumConverter());
    // jsonSerializer.Converters.Add(new JsonTimeOnlyConverter());

    // Temporary: Convert UTC → Jordan time in all responses.
    // Remove these 2 lines when mobile handles UTC → local conversion.
    jsonSerializer.Converters.Add(new JordanDateTimeJsonConverter());
    jsonSerializer.Converters.Add(new JordanNullableDateTimeJsonConverter());
}