using Application.Common.Models;
using Application.Ocpp.LiveCharging;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

/// <summary>
/// Sprint 5 — live charging (B), Docs/Sprint5-LiveCharging-B.md: CHG- plug stickers (§10), the admin session
/// list (§11.b), "problem with this session" (§13.1), session receipts (§13.2) and "the plug didn't work" (§13.3).
/// Driver info on the partner session list (§11.a) and the sticker start (§11.d) live in OcppRoutes.
/// </summary>
public static class LiveChargingSupportRoutes
{
    public record ConnectorReportRequest(string Reason, int? CommandId = null, string? Note = null);
    public record ResolveConnectorReportRequest(string? Note = null, bool AllOnPlug = true);
    public record SessionIssueRequest(string Reason, string? Note = null, string? ImageFileName = null);

    public static IEndpointRouteBuilder MapLiveChargingSupportRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/charging-points")
            .WithTags("Charging Points")
            .MapDriverStickerAndReportRoutes();

        app.MapGroup("/api/users/me/ocpp-sessions")
            .WithTags("Charging Points")
            .MapDriverIssueAndReceiptRoutes();

        app.MapGroup("/api/users/me/ocpp-settings")
            .WithTags("Charging Points")
            .MapDriverOcppSettingsRoutes();

        app.MapGroup("/api/receipts")
            .WithTags("Charging Points")
            .MapReceiptVerifyRoutes();

        app.MapGroup("/api/provider")
            .WithTags("Provider - Charging Points")
            .MapProviderLiveChargingRoutes();

        app.MapGroup("/api/admin/ocpp")
            .WithTags("OCPP")
            .MapAdminLiveChargingRoutes();

        app.MapGroup("/api/admin/users")
            .WithTags("OCPP")
            .MapAdminUserSessionRoutes();

        return app;
    }

    private static IResult File(LiveChargingFile f) => Results.File(f.Content, f.ContentType, f.FileName);

    // -----------------------------------------------------------------------
    // Driver app
    // -----------------------------------------------------------------------

    private static RouteGroupBuilder MapDriverStickerAndReportRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/connectors/resolve", async (IMediator mediator, [FromQuery] string? code, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new ResolveConnectorCodeRequest(code), ct)))
            .Produces<ConnectorCodeResolveDto>()
            .AllowAnonymous()   // also read by the landing's /c/{code} page (same origin; default CORS policy)
            .ProducesNotFound().ProducesCableValidationProblem(StatusCodes.Status410Gone).ProducesInternalServerError()
            .WithName("Resolve plug sticker code")
            .WithSummary("Driver app, no sign-in: what a scanned / typed CHG- code points at — station, charger (display name), plug, plug type, kW, state and canStartFromApp / cannotStartReason (offline | plugCable | busy | outOfOrder | noSubscription | notShared | blocked). code = the code or the full link https://cable-app.com/c/CHG-…, any case, spaces / dashes ignored. 404 code CODE_NOT_FOUND (not a Cable code), 410 code CODE_REVOKED (sticker replaced). Start with POST /api/users/me/ocpp-sessions/start + connectorCode.")
            .WithOpenApi();

        app.MapPost("/{chargingPointId:int}/chargers/{chargerId:int}/connectors/{connectorId:int}/reports",
                async (IMediator mediator, [FromRoute] int chargingPointId, [FromRoute] int chargerId, [FromRoute] int connectorId, ConnectorReportRequest body, CancellationToken ct) =>
                    Results.Ok(await mediator.Send(new ReportConnectorProblemCommand(chargingPointId, chargerId, connectorId, body.Reason, body.CommandId, body.Note), ct)))
            .Produces<ConnectorReportCreatedDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Report a plug that did not work")
            .WithSummary("Driver: the plug did not start — reason NoResponse | Rejected | ScreenError | Damaged | Other, commandId = the start's commandId (the charger's answer is attached), optional note. The station's owner and managers get a push. A second report on the same plug within 30 min returns the first (duplicate=true). openReports / suspectedIssue = the plug's state after this report.")
            .WithOpenApi();

        return app;
    }

    private static RouteGroupBuilder MapDriverIssueAndReceiptRoutes(this RouteGroupBuilder app)
    {
        app.MapPost("/{id:int}/issues/{issueId:int}/photo", async (IMediator mediator, [FromRoute] int id, [FromRoute] int issueId, [FromForm] IFormFile? file, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new UploadSessionIssuePhotoCommand(id, issueId, file), ct)))
            .Produces<SessionIssuePhotoDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Attach a photo to my session issue")
            .WithSummary("Driver: multipart, form key 'file', one jpg / png / webp up to 5 MB, attached to their own issue (replaces an earlier photo). The partner and admin view it with GET /api/usercomplaints/{issueId}/photo.")
            .WithOpenApi()
            .DisableAntiforgery();

        app.MapPost("/{id:int}/issues", async (IMediator mediator, [FromRoute] int id, SessionIssueRequest body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new ReportMySessionIssueCommand(id, body.Reason, body.Note, body.ImageFileName), ct)))
            .Produces<SessionIssueCreatedDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithName("Report a problem with my session")
            .WithSummary("Driver: \"problem with this session\" — reason WrongCost | StoppedEarly | NoEnergy | CableStuck | PlugBroken | Other, optional note (≤ 450); attach a photo afterwards with POST /{id}/issues/{issueId}/photo (imageFileName in the body is ignored). While the session runs or up to 7 days after it ended (422 ISSUE_WINDOW_CLOSED); one open issue per session (409 ISSUE_ALREADY_OPEN). It is a complaint (kind ChargingSession) in the partner's and admin's complaint lists; they get a push, and the driver gets one on every status change.")
            .WithOpenApi();

        app.MapGet("/{id:int}/issues", async (IMediator mediator, [FromRoute] int id, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetMySessionIssuesRequest(id), ct)))
            .Produces<List<SessionIssueDto>>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesNotFound().ProducesInternalServerError()
            .WithName("Get my session issues")
            .WithSummary("Driver: the problems reported on this session, newest first — status Open (sent) | InReview | Resolved | Closed with statusText in the request language and the partner's / admin's reply.")
            .WithOpenApi();

        app.MapGet("/{id:int}/receipt", async (IMediator mediator, [FromRoute] int id, CancellationToken ct) =>
                File(await mediator.Send(new GetMySessionReceiptRequest(id), ct)))
            .Produces(200, contentType: "application/pdf")
            .RequireAuthorization().ProducesUnAuthorized().ProducesNotFound().ProducesInternalServerError()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithName("Download my session receipt")
            .WithSummary("Driver: the PDF receipt of a finished session (CHS-YYYY-NNNNNN, assigned on the first download): station, charger, plug, times, battery, price lines and total (estimated — paid to the station), QR to the verification page. 422 SESSION_OPEN while it runs.")
            .WithOpenApi();

        return app;
    }

    /// <summary>§13.2: the driver's Cable Connect settings (email receipts).</summary>
    private static RouteGroupBuilder MapDriverOcppSettingsRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("", async (IMediator mediator, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetMyOcppSettingsRequest(), ct)))
            .Produces<DriverOcppSettingsDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesInternalServerError()
            .WithName("Get my charging settings")
            .WithSummary("Driver: { emailReceipts, hasEmail }. emailReceipts (default false) = email the receipt PDF when a session ends; nothing is sent while hasEmail is false.")
            .WithOpenApi();

        app.MapPut("", async (IMediator mediator, UpdateMyOcppSettingsCommand body, CancellationToken ct) =>
                Results.Ok(await mediator.Send(body, ct)))
            .Produces<DriverOcppSettingsDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Update my charging settings")
            .WithSummary("Driver: { emailReceipts: true|false } (null / missing = unchanged). Returns the saved settings. The email (subject and text in the device language, receipt PDF attached) goes out once per session, after the \"charging finished\" push.")
            .WithOpenApi(op => { op.RequestBody.Required = true; return op; });

        return app;
    }

    private static RouteGroupBuilder MapReceiptVerifyRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/verify/{receiptNo}", async (IMediator mediator, [FromRoute] string receiptNo, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new VerifySessionReceiptRequest(receiptNo), ct)))
            .Produces<SessionReceiptVerifyDto>()
            .AllowAnonymous()   // read by the landing's /r/{receiptNo} page (same origin; default CORS policy)
            .ProducesNotFound().ProducesInternalServerError()
            .WithName("Verify a charging receipt")
            .WithSummary("Public, no sign-in: is this receipt genuine — station, plug, times, energy, total and the driver's first name only. 404 code RECEIPT_NOT_FOUND. Read by the page the receipt's QR opens (https://cable-app.com/r/{receiptNo}).")
            .WithOpenApi();
        return app;
    }

    // -----------------------------------------------------------------------
    // Partner app / web
    // -----------------------------------------------------------------------

    private static RouteGroupBuilder MapProviderLiveChargingRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/charging-points/{chargingPointId:int}/connector-codes", async (IMediator mediator, [FromRoute] int chargingPointId, [FromQuery] int? chargerId, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetMyConnectorCodesRequest(chargingPointId, chargerId), ct)))
            .Produces<List<ConnectorCodeDto>>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Provider: plug sticker codes")
            .WithSummary("Owner only (not workers): the CHG- code and link of every plug at the station (plugs without one get it now).")
            .WithOpenApi();

        app.MapGet("/charging-points/{chargingPointId:int}/connector-codes/stickers.pdf", async (IMediator mediator, [FromRoute] int chargingPointId, [FromQuery] int? chargerId, CancellationToken ct) =>
                File(await mediator.Send(new GetMyStickerSheetRequest(chargingPointId, chargerId), ct)))
            .Produces(200, contentType: "application/pdf")
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Provider: print plug stickers")
            .WithSummary("Owner only (not workers): A4 sticker sheet — per plug the QR of its link, the code, station, charger display name, plug number and type. chargerId = one charger only.")
            .WithOpenApi();

        app.MapGet("/charging-points/{chargingPointId:int}/sessions/{transactionId:int}/receipt", async (IMediator mediator, [FromRoute] int chargingPointId, [FromRoute] int transactionId, CancellationToken ct) =>
                File(await mediator.Send(new GetStationSessionReceiptRequest(chargingPointId, transactionId), ct)))
            .Produces(200, contentType: "application/pdf")
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithName("Provider: session receipt")
            .WithSummary("Owner / manager (ConnectView): the same PDF receipt the driver gets, for a finished session at the station.")
            .WithOpenApi();

        app.MapGet("/charging-points/{chargingPointId:int}/connector-reports", async (IMediator mediator, [FromRoute] int chargingPointId, [FromQuery] string? status, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetStationConnectorReportsRequest(chargingPointId, status, page, pageSize), ct)))
            .Produces<PagedResult<ConnectorReportDto>>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Provider: plug reports")
            .WithSummary("Owner / manager (ConnectView): \"the plug didn't work\" reports, newest first. status = open (default) | resolved | all. The reporter shows as name + masked phone.")
            .WithOpenApi();

        app.MapPatch("/connector-reports/{id:int}/resolve", async (IMediator mediator, [FromRoute] int id, ResolveConnectorReportRequest? body, CancellationToken ct) =>
                Results.Ok(new { resolved = await mediator.Send(new ResolveConnectorReportCommand(id, body?.Note, body?.AllOnPlug ?? true), ct) }))
            .Produces(200)
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Provider: resolve plug report")
            .WithSummary("Owner / manager (ConnectControl) or admin: the plug is fixed — resolves every open report on that plug (allOnPlug=false: only this one) and clears its \"may have a problem\" warning. Returns { resolved: n }.")
            .WithOpenApi();

        return app;
    }

    // -----------------------------------------------------------------------
    // Admin
    // -----------------------------------------------------------------------

    private static AdminSessionFilter Filter(DateTime? from, DateTime? to, int? chargingPointId, int? chargerId, int? userId, string? source,
        string? startMethod, string? status, string? limitKind, string? q) =>
        new(from, to, chargingPointId, chargerId, userId, source ?? startMethod, status, limitKind, q);

    private static RouteGroupBuilder MapAdminLiveChargingRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/stations/{chargingPointId:int}/connector-codes", async (IMediator mediator, [FromRoute] int chargingPointId, [FromQuery] int? chargerId, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetAdminConnectorCodesRequest(chargingPointId, chargerId), ct)))
            .Produces<List<ConnectorCodeDto>>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Admin: plug sticker codes")
            .WithSummary("Admin: the active CHG- code of every plug at the station, with how many earlier codes were revoked. Plugs without a code get one now.")
            .WithOpenApi();

        app.MapPost("/stations/{chargingPointId:int}/connector-codes/{codeId:int}/renew", async (IMediator mediator, [FromRoute] int chargingPointId, [FromRoute] int codeId, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new RenewConnectorCodeCommand(chargingPointId, codeId), ct)))
            .Produces<ConnectorCodeDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Admin: revoke and renew plug code")
            .WithSummary("Admin: the sticker was stolen / shared — revoke this code (scans then answer 410 CODE_REVOKED) and issue a new one for the same plug. Print the new sticker.")
            .WithOpenApi();

        app.MapGet("/stations/{chargingPointId:int}/connector-codes/stickers.pdf", async (IMediator mediator, [FromRoute] int chargingPointId, [FromQuery] int? chargerId, CancellationToken ct) =>
                File(await mediator.Send(new GetAdminStickerSheetRequest(chargingPointId, chargerId), ct)))
            .Produces(200, contentType: "application/pdf")
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Admin: print plug stickers")
            .WithSummary("Admin: A4 sticker sheet for the station (or one charger) — QR of the link, the code, station, charger display name, plug number and type.")
            .WithOpenApi();

        app.MapGet("/sessions", async (IMediator mediator, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int? chargingPointId, [FromQuery] int? chargerId,
                    [FromQuery] int? userId, [FromQuery] string? source, [FromQuery] string? startMethod, [FromQuery] string? status, [FromQuery] string? limitKind,
                    [FromQuery] string? q, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetAdminOcppSessionsRequest(Filter(from, to, chargingPointId, chargerId, userId, source, startMethod, status, limitKind, q), page, pageSize), ct)))
            .Produces<AdminSessionsPageDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Admin: charging sessions")
            .WithSummary("Admin: every session, newest first. Filters: from / to (Jordan time), chargingPointId, chargerId, userId, source App | Sticker | Card | Operator, status open | closed, limitKind Jod | Kwh | Soc | none, q = session id / name / phone / idTag / card label; page / pageSize (20, max 200). totals = sessions, energyKwh, costJod over everything matched; openNow = running now under the same filters except status and dates.")
            .WithOpenApi();

        app.MapGet("/sessions/export", async (IMediator mediator, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int? chargingPointId, [FromQuery] int? chargerId,
                    [FromQuery] int? userId, [FromQuery] string? source, [FromQuery] string? startMethod, [FromQuery] string? status, [FromQuery] string? limitKind,
                    [FromQuery] string? q, CancellationToken ct) =>
                File(await mediator.Send(new ExportAdminOcppSessionsRequest(Filter(from, to, chargingPointId, chargerId, userId, source, startMethod, status, limitKind, q)), ct)))
            .Produces(200, contentType: "text/csv")
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Admin: export charging sessions")
            .WithSummary("Admin: the same filters as the list, as CSV (UTF-8 with BOM, Jordan time, at most 50,000 rows).")
            .WithOpenApi();

        app.MapGet("/sessions/{transactionId:int}", async (IMediator mediator, [FromRoute] int transactionId, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetAdminOcppSessionRequest(transactionId), ct)))
            .Produces<AdminSessionDetailDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .WithName("Admin: charging session detail")
            .WithSummary("Admin: one session with everything — driver (full phone, userId), staff who started it, card, station / charger / plug, times, energy, cost lines, battery, limit (who set it, when reached), stop reason and who stopped it, the RemoteStart / RemoteStop commands with the charger's answers, the readings curve (live.series) and its complaints.")
            .WithOpenApi();

        app.MapGet("/sessions/{transactionId:int}/receipt", async (IMediator mediator, [FromRoute] int transactionId, CancellationToken ct) =>
                File(await mediator.Send(new GetAdminSessionReceiptRequest(transactionId), ct)))
            .Produces(200, contentType: "application/pdf")
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesInternalServerError()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithName("Admin: session receipt")
            .WithSummary("Admin: the PDF receipt of any finished session.")
            .WithOpenApi();

        app.MapGet("/connector-reports", async (IMediator mediator, [FromQuery] string? status, [FromQuery] int? chargingPointId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetAdminConnectorReportsRequest(status, chargingPointId, from, to), ct)))
            .Produces<List<ConnectorReportGroupDto>>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Admin: plug reports")
            .WithSummary("Admin: \"the plug didn't work\" reports grouped per plug — open, today, total, suspectedIssue, the latest 5. status = open (plugs with an open report, default) | all; chargingPointId; from / to (Jordan time).")
            .WithOpenApi();

        app.MapPatch("/connector-reports/{id:int}/resolve", async (IMediator mediator, [FromRoute] int id, ResolveConnectorReportRequest? body, CancellationToken ct) =>
                Results.Ok(new { resolved = await mediator.Send(new ResolveConnectorReportCommand(id, body?.Note, body?.AllOnPlug ?? true), ct) }))
            .Produces(200)
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesNotFound().ProducesValidationProblem().ProducesInternalServerError()
            .WithName("Admin: resolve plug report")
            .WithSummary("Admin: same as the provider endpoint — the plug is fixed.")
            .WithOpenApi();

        return app;
    }

    private static RouteGroupBuilder MapAdminUserSessionRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/{userId:int}/ocpp-sessions", async (IMediator mediator, [FromRoute] int userId, [FromQuery] string? status, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetAdminOcppSessionsRequest(new AdminSessionFilter(UserId: userId, Status: status), page, pageSize), ct)))
            .Produces<AdminSessionsPageDto>()
            .RequireAuthorization().ProducesUnAuthorized().ProducesForbidden().ProducesInternalServerError()
            .WithName("Admin: user's charging sessions")
            .WithSummary("Admin, user profile tab: the sessions this user started (app, sticker, linked card — or as staff), newest first, with totals.")
            .WithOpenApi();
        return app;
    }
}
