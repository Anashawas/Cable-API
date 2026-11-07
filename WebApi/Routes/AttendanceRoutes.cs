using Application.Attendance.Holidays;
using Application.Attendance.Manual;
using Application.Attendance.Punching;
using Application.Attendance.Reports;
using Application.Attendance.Shifts;
using Application.Attendance.Zones;
using Cable.WebApi.OpenAPI;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

/// <summary>Worker tracker step 3: shifts, public holidays, punch zones, punching, who is in (WORKER_TRACKER §3.5–§3.8).</summary>
public static class AttendanceRoutes
{
    public record SetShiftsRequest(DateOnly? EffectiveFrom, List<ShiftDayDto> Days, int? GraceMinutes = null, int? EarliestPunchMinutes = null);
    public record ZoneWorkersRequest(List<int> WorkerIds);
    public record SetHolidaysRequest(int Year, List<PublicHolidayInput> Days);

    public static IEndpointRouteBuilder MapAttendanceRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/workers/{providerManagerId:int}/shifts").WithTags("Attendance - Shifts").MapShiftRoutes();
        app.MapGroup("/api/punch-zones").WithTags("Attendance - Punch zones").MapZoneRoutes();
        app.MapGroup("/api/attendance").WithTags("Attendance").MapPunchRoutes();
        app.MapGroup("/api/public-holidays").WithTags("Attendance - Public holidays").MapHolidayRoutes();
        app.MapGroup("/api/admin/public-holidays").WithTags("Attendance - Public holidays (admin)").MapAdminHolidayRoutes();
        return app;
    }

    private static RouteGroupBuilder MapShiftRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/", async (IMediator mediator, [FromRoute] int providerManagerId, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetWorkerShiftsRequest(providerManagerId), ct)))
            .Produces<WorkerShiftsDto>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Get Worker Shifts")
            .WithSummary("Owner, admin or the worker: the current weekly schedule, the upcoming one (if a change is scheduled) and today's shift window. Days: 0 = Sunday … 6 = Saturday; endTime ≤ startTime = crosses midnight.")
            .WithOpenApi();

        app.MapPut("/", async (IMediator mediator, [FromRoute] int providerManagerId, SetShiftsRequest body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new SetWorkerShiftsCommand(providerManagerId, body.EffectiveFrom, body.Days ?? [], body.GraceMinutes, body.EarliestPunchMinutes), ct)))
            .Produces<WorkerShiftsDto>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Set Worker Shifts")
            .WithSummary("Owner: the weekly schedule { effectiveFrom?, days: [{ dayOfWeek, startTime 'HH:mm', endTime, off }], graceMinutes? (10), earliestPunchMinutes? (60) }. Applies from effectiveFrom (default: today for a worker without a schedule, else next Sunday); past days never change; missing days = off. 403 FEATURE_NOT_IN_PLAN without the workers feature.")
            .WithOpenApi();
        return app;
    }

    private static RouteGroupBuilder MapZoneRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/", async (IMediator mediator, [FromQuery] string providerType, [FromQuery] int providerId, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetPunchZonesRequest(providerType, providerId), ct)))
            .Produces<List<PunchZoneDto>>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("List Punch Zones")
            .WithSummary("Owner or admin: the station's zones with their workers; appliesToNobody = disabled (no workers); warning = the radius hints (under 120 m / over 400 m).")
            .WithOpenApi();

        app.MapPost("/", async (IMediator mediator, SavePunchZoneCommand body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(body with { Id = null }, ct)))
            .Produces<PunchZoneDto>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Create Punch Zone")
            .WithSummary("Owner: { providerType, providerId, name, lat, lng, radiusMeters (50–800), workerIds? } — a circle around the station; punches are accepted inside it for the assigned workers.")
            .WithOpenApi();

        app.MapPut("/{id:int}", async (IMediator mediator, [FromRoute] int id, SavePunchZoneCommand body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(body with { Id = id }, ct)))
            .Produces<PunchZoneDto>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Update Punch Zone")
            .WithSummary("Owner: edit name, centre, radius, isActive, workerIds (when sent).")
            .WithOpenApi();

        app.MapDelete("/{id:int}", async (IMediator mediator, [FromRoute] int id, CancellationToken ct) =>
            {
                await mediator.Send(new DeletePunchZoneCommand(id), ct);
                return Results.Ok();
            })
            .Produces(200).RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Delete Punch Zone")
            .WithSummary("Owner: removes the zone (soft). Past punches keep their zone reference.")
            .WithOpenApi();

        app.MapPut("/{id:int}/workers", async (IMediator mediator, [FromRoute] int id, ZoneWorkersRequest body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new SetPunchZoneWorkersCommand(id, body.WorkerIds ?? []), ct)))
            .Produces<PunchZoneDto>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Set Punch Zone Workers")
            .WithSummary("Owner: which workers the zone applies to (providerManagerIds of this station). [] = applies to nobody.")
            .WithOpenApi();

        app.MapPost("/test", async (IMediator mediator, TestPunchZoneRequest body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(body, ct)))
            .Produces<ZoneTestDto>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Test Punch Zone")
            .WithSummary("Owner: \"try from where you stand\" — { providerType, providerId, lat, lng, accuracyMeters, isMocked? } → distance to the nearest zone and wouldBe accepted | review | rejected with the reason. Nothing is recorded.")
            .WithOpenApi();

        app.MapGet("/attempts", async (IMediator mediator, [FromQuery] string providerType, [FromQuery] int providerId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? verdict, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetPunchAttemptsRequest(providerType, providerId, from, to, verdict), ct)))
            .Produces<List<PunchAttemptDto>>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("List Punch Attempts")
            .WithSummary("Owner, admin or ViewAttendance: refused (rejected) and review punches, newest first; from/to in Jordan time (default: last 30 days); verdict = rejected | review.")
            .WithOpenApi();
        return app;
    }

    private static RouteGroupBuilder MapPunchRoutes(this RouteGroupBuilder app)
    {
        app.MapPost("/punch", async (IMediator mediator, PunchCommand body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(body, ct)))
            .Produces<PunchResult>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity).ProducesInternalServerError()
            .WithName("Punch")
            .WithSummary("Worker (Attendance privilege): { providerType, providerId, direction in | out, lat, lng, accuracyMeters, isMocked, deviceId }. No time is sent — the server stamps it. 422 OUTSIDE_ZONE (distanceMeters, zoneName, radiusMeters) | LOW_ACCURACY | TOO_EARLY (shiftStart, earliestAt); 409 ALREADY_IN (since) | NOT_IN; 403 WORKER_INACTIVE | PRIVILEGE_MISSING | FEATURE_NOT_IN_PLAN. verdict review = recorded, awaiting the owner.")
            .WithOpenApi();

        app.MapGet("/today", async (IMediator mediator, [FromQuery] string providerType, [FromQuery] int providerId, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetMyAttendanceTodayRequest(providerType, providerId), ct)))
            .Produces<AttendanceTodayDto>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("My Attendance Today")
            .WithSummary("Worker: the \"my shift\" screen — state in | out | finished | off, today's shift, punched in since, worked minutes (live), earliestAt, canPunchIn / canPunchOut, a ready hint, and the last 7 days.")
            .WithOpenApi();

        app.MapGet("/who-is-in", async (IMediator mediator, [FromQuery] string providerType, [FromQuery] int providerId, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetWhoIsInRequest(providerType, providerId), ct)))
            .Produces<List<WhoIsInRowDto>>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Who Is In")
            .WithSummary("Owner, admin or ViewAttendance: every usable worker with state in | late | notPunched | beforeShift | out | off | holiday, since, lateMinutes, zone, shift, worked minutes, review flag.")
            .WithOpenApi();

        // ---- step 4: table, export, history, manual entry, corrections ----
        app.MapGet("/", async (IMediator mediator, [FromQuery] string providerType, [FromQuery] int providerId, [FromQuery] int? workerId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? status, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetAttendanceTableRequest(providerType, providerId, workerId, from, to, status), ct)))
            .Produces<AttendanceTableDto>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Attendance Table")
            .WithSummary("Owner, admin or ViewAttendance: one row per worker per day (stored days plus absent / off / holiday days worked out from the shifts), newest first, with the period summary per worker. Default range: this month; max 92 days. status filter: present | missingOut | absent | leave | holiday | off.")
            .WithOpenApi();

        app.MapGet("/export", async (IMediator mediator, [FromQuery] string providerType, [FromQuery] int providerId, [FromQuery] int? workerId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? status, CancellationToken ct) =>
            {
                var f = await mediator.Send(new GetAttendanceCsvRequest(providerType, providerId, workerId, from, to, status), ct);
                return Results.File(f.Content, f.ContentType, f.FileName);
            })
            .Produces(200, contentType: "text/csv").RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Attendance Export")
            .WithSummary("Owner, admin or ViewAttendance: the table as CSV (UTF-8 with BOM) for payroll — days, then the per-worker summary. Column 'Method' = app | manual | correction.")
            .WithOpenApi();

        app.MapGet("/my", async (IMediator mediator, [FromQuery] string providerType, [FromQuery] int providerId, [FromQuery] string? month, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetMyAttendanceHistoryRequest(providerType, providerId, month), ct)))
            .Produces<AttendanceTableDto>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("My Attendance History")
            .WithSummary("Worker: my days of a month (month = YYYY-MM, default this month) with the summary; corrected days carry the 'corrected' flag.")
            .WithOpenApi();

        app.MapPost("/manual", async (IMediator mediator, ManualPunchCommand body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(body, ct)))
            .Produces<ManualPunchResult>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity).ProducesInternalServerError()
            .WithName("Manual Punch")
            .WithSummary("Owner, or a worker with ManageAttendance: { providerType, providerId, workerId, direction in | out, note } — starts / ends another worker's shift now. 422 NOTE_REQUIRED; 403 CANNOT_RECORD_SELF; 409 ALREADY_IN | NOT_IN. The owner is pushed when a supervisor did it; the day is flagged 'manual'.")
            .WithOpenApi();

        app.MapPost("/days", async (IMediator mediator, CreateAttendanceDayCommand body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(body, ct)))
            .Produces<AttendanceDayRowDto>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).ProducesInternalServerError()
            .WithName("Create Attendance Day")
            .WithSummary("Owner: add a day that has no row { providerType, providerId, workerId, workDate, inAt?, outAt?, status?, note } (Jordan times), then the same rules as a correction.")
            .WithOpenApi();

        app.MapPatch("/days/{id:int}", async (IMediator mediator, [FromRoute] int id, CorrectAttendanceDayCommand body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(body with { AttendanceDayId = id }, ct)))
            .Produces<AttendanceDayRowDto>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).ProducesInternalServerError()
            .WithName("Correct Attendance Day")
            .WithSummary("Owner only: { inAt?, outAt?, status?, confirmReview?, note } (Jordan times). Sets the first in / last out (added when missing), the status, or confirms a review punch. Every change goes to the history with the note; the day is flagged 'corrected'; the worker is pushed. 422 NOTE_REQUIRED. Nothing is deleted.")
            .WithOpenApi();

        app.MapGet("/days/{id:int}/history", async (IMediator mediator, [FromRoute] int id, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetAttendanceDayHistoryRequest(id), ct)))
            .Produces<AttendanceDayHistoryDto>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Attendance Day History")
            .WithSummary("Owner, admin, ViewAttendance or the worker: the day's punches (source, zone, distance, who recorded) and every correction (field, from → to, note, who, when).")
            .WithOpenApi();
        return app;
    }

    private static RouteGroupBuilder MapHolidayRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/", async (IMediator mediator, [FromQuery] int? year, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetPublicHolidaysRequest(year), ct)))
            .Produces<List<PublicHolidayDto>>().RequireAuthorization().ProducesUnAuthorized().ProducesInternalServerError()
            .WithName("List Public Holidays")
            .WithSummary("Any signed-in user: the Jordan public holidays of a year (default this year).")
            .WithOpenApi();
        return app;
    }

    private static RouteGroupBuilder MapAdminHolidayRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/", async (IMediator mediator, [FromQuery] int? year, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetPublicHolidaysRequest(year), ct)))
            .Produces<List<PublicHolidayDto>>().RequireAuthorization().ProducesUnAuthorized().ProducesInternalServerError()
            .WithName("List Public Holidays (admin)")
            .WithOpenApi();

        app.MapPut("/", async (IMediator mediator, SetHolidaysRequest body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new SetPublicHolidaysCommand(body.Year, body.Days ?? []), ct)))
            .Produces<List<PublicHolidayDto>>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Set Public Holidays")
            .WithSummary("Admin: { year, days: [{ date, nameAr, nameEn }] } — the year's full list; dates left out are removed.")
            .WithOpenApi();
        return app;
    }
}
