using System.Reflection;
using Application.Common.Behaviours;
using Microsoft.Extensions.DependencyInjection;
using FluentValidation;
using MediatR;

namespace Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
        services.AddMediatR(op => { op.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly()); });

        services.AddAutoMapper(Assembly.GetExecutingAssembly());

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(UnhandledExceptionBehaviour<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehaviour<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehaviour<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PerformanceBehaviour<,>));
        
        
        // Shared by RecordPayment and the legacy premium endpoint.
        services.AddScoped<Subscriptions.PaymentRecorder>();

        // Sprint 5 live charging (A): automatic points when a driver's session ends (Hangfire job, run by the API).
        services.AddScoped<Ocpp.Points.IOcppSessionPointsService, Ocpp.Points.OcppSessionPointsService>();

        return services;
    }
}