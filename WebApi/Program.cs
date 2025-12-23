using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application;
using Application.Common.Interfaces;
using Cable.Core.Emuns;
using Cable.Identity;
using Cable.Routes;
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
app.UseAuthorization();
app.UseSecureFileServing();
app.MapHangfireDashboard("/Cable-Jobs-Dashboard");

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
    .MapSharedLinksRoutes();
app.Run();

void ConfigureJsonSerliaizer(JsonSerializerOptions jsonSerializer)
{
    jsonSerializer.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    jsonSerializer.NumberHandling = JsonNumberHandling.AllowReadingFromString;
    jsonSerializer.ReadCommentHandling = JsonCommentHandling.Skip;
    jsonSerializer.AllowTrailingCommas = true;
    jsonSerializer.Converters.Add(new JsonStringEnumConverter());
    // jsonSerializer.Converters.Add(new JsonTimeOnlyConverter());
}