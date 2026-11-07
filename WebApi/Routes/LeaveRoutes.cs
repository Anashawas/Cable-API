using Application.Attendance.Leave;
using Cable.WebApi.OpenAPI;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

/// <summary>Worker tracker step 5: leave (WORKER_TRACKER §3.9).</summary>
public static class LeaveRoutes
{
    public record DecideLeaveRequest(string Status, string? Note);
    public record LeaveSettingsRequest(int AnnualLeaveDays, int SickLeaveDays);

    public static IEndpointRouteBuilder MapLeaveRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/leave").WithTags("Attendance - Leave").MapRoutes();
        app.MapGroup("/api/workers/{providerManagerId:int}/leave-settings").WithTags("Attendance - Leave")
            .MapPut("/", async (IMediator mediator, [FromRoute] int providerManagerId, LeaveSettingsRequest body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new SetLeaveSettingsCommand(providerManagerId, body.AnnualLeaveDays, body.SickLeaveDays), ct)))
            .Produces<LeaveBalanceDto>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Set Leave Settings")
            .WithSummary("Owner: the worker's yearly balances { annualLeaveDays, sickLeaveDays } (defaults 14 / 14, 0–90). Returns this year's balance.")
            .WithOpenApi();
        return app;
    }

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/balance", async (IMediator mediator, [FromQuery] string providerType, [FromQuery] int providerId, [FromQuery] int? workerId, [FromQuery] int? year, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetLeaveBalanceRequest(providerType, providerId, workerId, year), ct)))
            .Produces<LeaveBalanceDto>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Leave Balance")
            .WithSummary("The worker (own) or owner / admin / ApproveLeave (workerId): { year, annual: { total, used, pending, remaining }, sick: {…}, unpaidUsed }. Year = 1 January to 31 December; no carry-over.")
            .WithOpenApi();

        app.MapGet("/", async (IMediator mediator, [FromQuery] string providerType, [FromQuery] int providerId, [FromQuery] int? workerId, [FromQuery] string? status, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetLeaveRequestsRequest(providerType, providerId, workerId, status, from, to), ct)))
            .Produces<List<LeaveRequestDto>>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("List Leave Requests")
            .WithSummary("A worker sees their own; owner / admin / ApproveLeave see everyone's. Pending first. Each row says canApprove / canCancel for the caller. status = pending | approved | rejected | cancelled.")
            .WithOpenApi();

        app.MapPost("/preview", async (IMediator mediator, PreviewLeaveRequest body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(body, ct)))
            .Produces<LeavePreviewDto>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Preview Leave")
            .WithSummary("Before sending: { providerType, providerId, workerId?, kind paid | unpaid | sick, fromDate, toDate } → { daysCounted, remainingAfter, days[] } — off days and public holidays are left out.")
            .WithOpenApi();

        app.MapPost("/", async (IMediator mediator, CreateLeaveRequestCommand body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(body, ct)))
            .Produces<LeaveRequestDto>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity).ProducesInternalServerError()
            .WithName("Request Leave")
            .WithSummary("Worker (Attendance): { providerType, providerId, kind, fromDate, toDate, reason? } → pending. Paid / unpaid from today on; sick may be backdated 7 days; max 60 days; within one year. 422 INSUFFICIENT_BALANCE (remaining, requested, pending); 409 OVERLAPS_EXISTING. The owner and ApproveLeave workers are pushed.")
            .WithOpenApi();

        app.MapPost("/direct", async (IMediator mediator, DirectLeaveCommand body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(body, ct)))
            .Produces<LeaveRequestDto>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity).ProducesInternalServerError()
            .WithName("Direct Leave")
            .WithSummary("Owner: { providerType, providerId, workerId, kind, fromDate, toDate, reason? } — approved at once (may be backdated 30 days); the days become 'leave' in attendance; the worker is pushed.")
            .WithOpenApi();

        app.MapPatch("/{id:int}", async (IMediator mediator, [FromRoute] int id, DecideLeaveRequest body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new DecideLeaveCommand(id, body.Status, body.Note), ct)))
            .Produces<LeaveRequestDto>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity).ProducesInternalServerError()
            .WithName("Decide Leave")
            .WithSummary("{ status: approved | rejected | cancelled, note? }. Approve / reject: owner, admin or ApproveLeave — never one's own (403 CANNOT_DECIDE_OWN); a supervisor's request is the owner's only (403 OWNER_ONLY); 422 LEAVE_NOT_PENDING. Cancel: the worker (pending, or approved before it starts — 422 LEAVE_ALREADY_STARTED) or the owner; the balance comes back and the leave days are removed from attendance.")
            .WithOpenApi();

        app.MapPost("/{id:int}/attachment", async (IMediator mediator, [FromRoute] int id, [FromForm] IFormFile? file, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new UploadLeaveAttachmentCommand(id, file), ct)))
            .Produces<LeaveRequestDto>().RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).ProducesInternalServerError()
            .WithName("Upload Leave Attachment")
            .WithSummary("The worker or the owner: multipart, form key 'file' (jpg / png / webp / pdf ≤ 5 MB) on a pending or approved sick leave (422 ATTACHMENT_NOT_ALLOWED otherwise). Replaces an earlier one. Deleted a year after the leave.")
            .WithOpenApi()
            .DisableAntiforgery();

        app.MapGet("/{id:int}/attachment", async (IMediator mediator, [FromRoute] int id, CancellationToken ct) =>
            {
                var f = await mediator.Send(new GetLeaveAttachmentRequest(id), ct);
                return Results.File(f.Content, f.ContentType, f.FileName);
            })
            .Produces(200, contentType: "application/octet-stream").RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Get Leave Attachment")
            .WithSummary("Owner, admin or the worker only (a supervisor sees the kind and dates, never the report).")
            .WithOpenApi();
        return app;
    }
}
