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
        // Sprint 5 live charging (B): pushes run by Hangfire (plug reports, session issues).
        services.AddScoped<Ocpp.LiveCharging.LiveChargingNotifications>();
        services.AddScoped<Workers.WorkerAuth.WorkerLoginNotifier>();
        services.AddScoped<Attendance.AttendanceNotifier>();
        services.AddScoped<Attendance.Jobs.IAttendanceJobs, Attendance.Jobs.AttendanceJobs>();
        // §13.2: the receipt email after a driver's session ends (opt-in), run by the "finished" Hangfire job.
        services.AddScoped<Ocpp.LiveCharging.SessionReceiptEmailer>();
        // Partner billing (Sprint 5): plan subscriptions on top of PaymentRecorder, and its Hangfire jobs.
        services.AddScoped<Billing.Subscriptions.PlanSubscriptionService>();
        services.AddScoped<Billing.Jobs.IBillingJobs, Billing.Jobs.BillingJobs>();
        // Promo codes at partner-scan time (PreviewPartnerCode / ScanPartnerCode).
        services.AddScoped<Promos.PromoApplicator>();

        return services;
    }
}