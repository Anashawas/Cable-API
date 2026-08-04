using Application.ChargingPoints.Commands.AddUpdateRequestAttachments;
using Application.ChargingPoints.Commands.ApproveUpdateRequest;
using Application.ChargingPoints.Commands.CancelUpdateRequest;
using Application.ChargingPoints.Commands.RejectUpdateRequest;
using Application.ChargingPoints.Commands.SubmitChargingPointUpdateRequest;
using Application.ChargingPoints.Commands.UploadUpdateRequestIcon;
using Application.ChargingPoints.Queries;
using Application.ChargingPoints.Queries.GetMyChargingPoints;
using Application.ChargingPoints.Queries.GetMyUpdateRequests;
using Application.ChargingPoints.Queries.GetPendingUpdateRequests;
using Application.ChargingPoints.Queries.GetUpdateRequestById;
using Application.Common.Interfaces;
using Application.Common.Models;
using Application.Providers.Commands.SendFavoritesNotification;
using Application.Providers.Commands.SetAutoApproveWorkerNotifications;
using Application.Providers.Queries.GetFavoritesNotificationHistory;
using Application.Providers.Queries.GetMyProviderAssets;
using Application.Providers.Queries.GetProviderFavoritedUsers;
using Application.ServiceProviders.Commands.AddServiceProviderAttachment;
using Application.ServiceProviders.Commands.CreateServiceProvider;
using Application.ServiceProviders.Commands.DeleteServiceProvider;
using Application.ServiceProviders.Commands.DeleteServiceProviderAttachment;
using Application.ServiceProviders.Commands.UpdateServiceProvider;
using Application.ServiceProviders.Commands.UploadServiceProviderIcon;
using Application.ServiceProviders.Queries.GetAllServiceProviders;
using Application.ServiceProviders.Queries.GetMyServiceProviders;
using Cable.Requests.ChargingPoints;
using Cable.Requests.Providers;
using Cable.Requests.ServiceProviders;
using Cable.WebApi.OpenAPI;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class ProviderRoutes
{
    public static IEndpointRouteBuilder MapProviderRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/provider")
            .WithTags("Provider")
            .MapAuthenticationRoutes()
            .MapDashboardRoutes()
            .MapChargingPointManagementRoutes()
            .MapServiceProviderManagementRoutes();

        return app;
    }

    #region Authentication

    private static RouteGroupBuilder MapAuthenticationRoutes(this RouteGroupBuilder app)
    {
        // Step 1: Authenticate with email and password
        app.MapPost("/authenticate", async (
                ProviderLoginRequest request,
                IAuthenticationService authenticationService,
                CancellationToken cancellationToken) =>
                Results.Ok(await authenticationService.LoginProvider(
                    request.Email,
                    request.Password,
                    cancellationToken))
            )
            .AddEndpointFilter<ProviderLoginRequestValidationFilter>()
            .Produces<ProviderAuthSessionResult>()
            .ProducesValidationProblem()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Authenticate Provider Email")
            .WithSummary("Step 1: Authenticates Provider user with email and password (2FA)")
            .WithDescription("Validates Provider user credentials and returns a session token for OTP verification. Provider users must have verified phone numbers.")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                op.Responses["200"].Description = "Email and password verified successfully. Session token returned for OTP step.";
                return op;
            });

        // Step 2: Send OTP to verified phone
        app.MapPost("/send-otp", async (
                ProviderSendOtpRequest request,
                IAuthenticationService authenticationService,
                CancellationToken cancellationToken) =>
                Results.Ok(await authenticationService.SendProviderOtpAsync(
                    request.SessionToken,
                    cancellationToken))
            )
            .AddEndpointFilter<ProviderSendOtpRequestValidationFilter>()
            .Produces<string>()
            .ProducesValidationProblem()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Send Provider OTP")
            .WithSummary("Step 2: Sends OTP to Provider user's verified phone number")
            .WithDescription("Sends a 6-digit OTP code to the phone number associated with the session token from Step 1.")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                op.Responses["200"].Description = "OTP sent successfully to registered phone number";
                return op;
            });

        // Step 3: Verify OTP and complete login
        app.MapPost("/verify-otp", async (
                ProviderVerifyOtpRequest request,
                IAuthenticationService authenticationService,
                CancellationToken cancellationToken) =>
                Results.Ok(await authenticationService.VerifyProviderOtpAsync(
                    request.SessionToken,
                    request.OtpCode,
                    cancellationToken))
            )
            .AddEndpointFilter<ProviderVerifyOtpRequestValidationFilter>()
            .Produces<UserLoginResult>()
            .ProducesValidationProblem()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Verify Provider OTP")
            .WithSummary("Step 3: Verifies OTP and completes Provider user login")
            .WithDescription("Validates the 6-digit OTP code and returns JWT tokens for authenticated session.")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                op.Responses["200"].Description = "OTP verified successfully. JWT tokens returned.";
                return op;
            });

        app.MapPost("/logout", async (IAuthenticationService authenticationService,
                    ICurrentUserService currentUserService, CancellationToken cancellationToken) =>
                {
                    await authenticationService.Logout(currentUserService.UserId!.Value, cancellationToken);
                    return Results.Ok();
                })
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Provider Logout")
            .WithSummary("Logs out the provider by invalidating all active sessions")
            .WithOpenApi();

        return app;
    }

    #endregion

    #region Dashboard

    private static RouteGroupBuilder MapDashboardRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/my-assets", async (IMediator mediator, CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(new GetMyProviderAssetsRequest(), cancellationToken)))
            .Produces<ProviderAssetsDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get My Provider Assets")
            .WithSummary("Get all assets owned by the current provider")
            .WithDescription("Returns both charging points and service providers owned by the currently authenticated user.")
            .WithOpenApi();

        app.MapGet("/favorites/{providerType}/{providerId:int}",
                async (IMediator mediator, [FromRoute] string providerType, [FromRoute] int providerId,
                        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(
                        new GetProviderFavoritedUsersRequest(providerType, providerId, page, pageSize),
                        cancellationToken)))
            .Produces<PagedResult<FavoritedUserDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Get Provider Favorited Users")
            .WithSummary("Users who favorited a charging point or service provider (owner, worker, or admin only)")
            .WithDescription("providerType is 'ChargingPoint' or 'ServiceProvider'. Returns userId, name, city and when they favorited — no contact details. Optional page/pageSize.")
            .WithOpenApi();

        app.MapPost("/favorites/{providerType}/{providerId:int}/notify",
                async (IMediator mediator, [FromRoute] string providerType, [FromRoute] int providerId,
                        SendFavoritesNotificationRequest request, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(
                        new SendFavoritesNotificationCommand(providerType, providerId, request.Title, request.Body,
                            request.NotificationTypeId),
                        cancellationToken)))
            .Produces<SendFavoritesNotificationResult>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Send Favorites Notification")
            .WithSummary("Announce to every user who favorited this provider (owner/worker/admin; max 2 actual sends per 24h). Routing (deepLink + FCM type/chargerId) is attached server-side")
            .WithDescription("providerType is 'ChargingPoint' or 'ServiceProvider'. Body: { notificationTypeId, body } — the title is then built server-side as '{typeName} From {stationName}'; a raw { title, body } still works. A WORKER's send is stored as status 'pending' until the owner/admin approves it, unless autoApproveWorkerNotifications is on for the provider.")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });

        app.MapGet("/favorites/{providerType}/{providerId:int}/notifications",
                async (IMediator mediator, [FromRoute] string providerType, [FromRoute] int providerId,
                        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(
                        new GetFavoritesNotificationHistoryRequest(providerType, providerId, false, page, pageSize),
                        cancellationToken)))
            .Produces<PagedResult<FavoritesNotificationHistoryDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Get Favorites Notification History")
            .WithSummary("F1: announcements sent for this asset — 'reached N users, read by M' (owner/worker/admin, newest first, optional paging)")
            .WithDescription("Each row: title, body, status (pending/sent/rejected), notificationType, sender, submittedAt/sentAt/decidedAt, recipientCount, deliveredCount (FCM-accepted), readCount (from the in-app inbox).")
            .WithOpenApi();

        app.MapGet("/favorites/{providerType}/{providerId:int}/notifications/pending",
                async (IMediator mediator, [FromRoute] string providerType, [FromRoute] int providerId,
                        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(
                        new GetFavoritesNotificationHistoryRequest(providerType, providerId, true, page, pageSize),
                        cancellationToken)))
            .Produces<PagedResult<FavoritesNotificationHistoryDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Get Pending Favorites Notifications")
            .WithSummary("F3: worker announcements awaiting owner/admin approval for this asset")
            .WithOpenApi();

        app.MapPut("/favorites/notifications/{notificationId:int}/approve",
                async (IMediator mediator, [FromRoute] int notificationId, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new DecideFavoritesNotificationCommand(notificationId, true),
                        cancellationToken)))
            .Produces<DecideFavoritesNotificationResult>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Approve Favorites Notification")
            .WithSummary("F3: owner/admin approves a worker's pending announcement — it is delivered NOW (rate limit checked at this moment)")
            .WithOpenApi();

        app.MapPut("/favorites/notifications/{notificationId:int}/reject",
                async (IMediator mediator, [FromRoute] int notificationId, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new DecideFavoritesNotificationCommand(notificationId, false),
                        cancellationToken)))
            .Produces<DecideFavoritesNotificationResult>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Reject Favorites Notification")
            .WithSummary("F3: owner/admin rejects a worker's pending announcement — nothing is delivered")
            .WithOpenApi();

        app.MapPut("/favorites/{providerType}/{providerId:int}/auto-approve-worker-notifications",
                async (IMediator mediator, [FromRoute] string providerType, [FromRoute] int providerId,
                        SetAutoApproveRequest request, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(
                        new SetAutoApproveWorkerNotificationsCommand(providerType, providerId, request.Enabled),
                        cancellationToken)))
            .Produces<bool>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Set Auto Approve Worker Notifications")
            .WithSummary("F4: owner/admin toggles direct sending for this provider's workers (default off). Body: { enabled }")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });

        return app;
    }

    #endregion

    #region Charging Point Management

    private static RouteGroupBuilder MapChargingPointManagementRoutes(this RouteGroupBuilder app)
    {
        // Get my charging points
        app.MapGet("/charging-points/my",
                async (IMediator mediator, [FromQuery] int? chargerPointTypeId, [FromQuery] string? cityName,
                        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
                {
                    var paged = await mediator.Send(new GetMyChargingPointsRequest(chargerPointTypeId, cityName, page, pageSize),
                        cancellationToken);
                    return Results.Ok(page.HasValue || pageSize.HasValue ? (object)paged : paged.Items);
                })
            .Produces<List<GetAllChargingPointsDto>>()
            .Produces<PagedResult<GetAllChargingPointsDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get My Charging Points")
            .WithSummary("Get charging points owned by the current provider")
            .WithOpenApi();

        // Submit Update Request (Data only, no files)
        app.MapPost("/charging-points/submit-update-request/{chargingPointId:int}",
                async (IMediator mediator,
                       [FromRoute] int chargingPointId,
                       SubmitChargingPointUpdateRequest request,
                       CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new SubmitChargingPointUpdateRequestCommand(
                        chargingPointId,
                        request.Name,
                        request.Note,
                        request.CountryName,
                        request.CityName,
                        request.Phone,
                        request.MethodPayment,
                        request.Price,
                        request.FromTime,
                        request.ToTime,
                        request.ChargerSpeed,
                        request.ChargersCount,
                        request.Latitude,
                        request.Longitude,
                        request.StatusId,
                        request.OwnerPhone,
                        request.Service,
                        request.OfferDescription,
                        request.Address,
                        request.PlugTypeIds,
                        request.AttachmentsToDelete
                    ), cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Submit Charging Point Update Request")
            .WithSummary("Provider owners submit update request for their charging point (data only)")
            .WithDescription("Creates a pending update request that requires admin approval. After creating the request, use separate endpoints to upload icon and attachments. Only one pending request allowed per charging point.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the charging point to update";
                op.RequestBody.Required = true;
                return op;
            });

        // Upload Icon for Update Request
        app.MapPost("/charging-points/upload-update-request-icon/{updateRequestId:int}",
                async (IMediator mediator,
                       [FromRoute] int updateRequestId,
                       [FromForm] IFormFile file,
                       CancellationToken cancellationToken) =>
                    await mediator.Send(new UploadUpdateRequestIconCommand(file, updateRequestId), cancellationToken))
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Upload Update Request Icon")
            .WithSummary("Upload icon for a pending update request")
            .WithDescription("Provider owners can upload an icon to their pending update request. Replaces any previously uploaded icon for this request.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the update request";
                return op;
            })
            .DisableAntiforgery();

        // Add Attachments to Update Request
        app.MapPost("/charging-points/add-update-request-attachments/{updateRequestId:int}",
                async (IMediator mediator,
                       [FromRoute] int updateRequestId,
                       [FromForm] IFormFileCollection files,
                       CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new AddUpdateRequestAttachmentsCommand(updateRequestId, files), cancellationToken)))
            .Produces<int[]>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Add Update Request Attachments")
            .WithSummary("Add attachments to a pending update request")
            .WithDescription("Provider owners can add multiple attachments to their pending update request. Returns array of created attachment IDs.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the update request";
                return op;
            })
            .DisableAntiforgery();

        // Get My Update Requests
        app.MapPost("/charging-points/update-requests/my-requests",
                async (IMediator mediator, GetMyUpdateRequestsRequest request, CancellationToken cancellationToken) =>
                {
                    var paged = await mediator.Send(request, cancellationToken);
                    return Results.Ok(request.Page.HasValue || request.PageSize.HasValue ? (object)paged : paged.Items);
                })
            .Produces<List<GetPendingUpdateRequestsDto>>()
            .Produces<PagedResult<GetPendingUpdateRequestsDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get My Update Requests")
            .WithSummary("Get current user's update requests")
            .WithDescription("Returns all update requests submitted by the currently logged-in user. Can filter by status.")
            .WithOpenApi();

        // Get Update Request By ID
        app.MapGet("/charging-points/update-requests/{id:int}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetUpdateRequestByIdRequest(id), cancellationToken)))
            .Produces<GetUpdateRequestByIdDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Get Update Request By ID")
            .WithSummary("Get detailed update request information")
            .WithDescription("Returns detailed information about a specific update request including both proposed changes and current values for comparison.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the update request";
                return op;
            });

        // Cancel Update Request (Owner)
        app.MapDelete("/charging-points/update-requests/{id:int}/cancel",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new CancelUpdateRequestCommand(id), cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Cancel Update Request")
            .WithSummary("Cancel your own pending update request")
            .WithDescription("Allows the request owner to cancel their pending update request. Deletes all uploaded files and soft-deletes the request. Only pending requests can be cancelled.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the update request to cancel";
                return op;
            });

        // Get Pending Update Requests (Admin)
        app.MapPost("/charging-points/update-requests/pending",
                async (IMediator mediator, GetPendingUpdateRequestsRequest request, CancellationToken cancellationToken) =>
                {
                    var paged = await mediator.Send(request, cancellationToken);
                    return Results.Ok(request.Page.HasValue || request.PageSize.HasValue ? (object)paged : paged.Items);
                })
            .Produces<List<GetPendingUpdateRequestsDto>>()
            .Produces<PagedResult<GetPendingUpdateRequestsDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get Pending Update Requests")
            .WithSummary("Get all update requests (for admin)")
            .WithDescription("Returns all charging point update requests. Can filter by status (Pending, Approved, Rejected).")
            .WithOpenApi();

        // Approve Update Request (Admin)
        app.MapPost("/charging-points/update-requests/{id:int}/approve",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new ApproveUpdateRequestCommand(id), cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Approve Update Request")
            .WithSummary("Approve a pending update request (Admin only)")
            .WithDescription("Applies all proposed changes to the charging point, including field updates, icon changes, plug type changes, and attachment modifications. Only pending requests can be approved.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the update request to approve";
                return op;
            });

        // Reject Update Request (Admin)
        app.MapPost("/charging-points/update-requests/{id:int}/reject",
                async (IMediator mediator, [FromRoute] int id, RejectUpdateRequest request, CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new RejectUpdateRequestCommand(id, request.RejectionReason), cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Reject Update Request")
            .WithSummary("Reject a pending update request (Admin only)")
            .WithDescription("Rejects the update request, deletes all uploaded files, and stores the rejection reason. Only pending requests can be rejected.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the update request to reject";
                op.RequestBody.Required = true;
                return op;
            });

        return app;
    }

    #endregion

    #region Service Provider Management

    private static RouteGroupBuilder MapServiceProviderManagementRoutes(this RouteGroupBuilder app)
    {
        // Get my service providers
        app.MapGet("/service-providers/my",
                async (IMediator mediator, [FromQuery] int? categoryId,
                        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
                {
                    var paged = await mediator.Send(new GetMyServiceProvidersRequest(categoryId, page, pageSize), cancellationToken);
                    return Results.Ok(page.HasValue || pageSize.HasValue ? (object)paged : paged.Items);
                })
            .Produces<List<ServiceProviderDto>>()
            .Produces<PagedResult<ServiceProviderDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get My Service Providers")
            .WithSummary("Get service providers owned by the current provider")
            .WithDescription("Returns all service providers owned by the currently authenticated user. Optional filter by category ID.")
            .WithOpenApi();

        // Create service provider
        app.MapPost("/service-providers/create",
                async (IMediator mediator, CreateServiceProviderRequest request,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new CreateServiceProviderCommand(
                        request.Name,
                        request.ServiceCategoryId,
                        request.StatusId,
                        request.Description,
                        request.Phone,
                        request.OwnerPhone,
                        request.Address,
                        request.CountryName,
                        request.CityName,
                        request.Latitude,
                        request.Longitude,
                        request.Price,
                        request.PriceDescription,
                        request.FromTime,
                        request.ToTime,
                        request.MethodPayment,
                        request.HasOffer,
                        request.OfferDescription,
                        request.Service,
                        request.Note,
                        request.WhatsAppNumber,
                        request.WebsiteUrl
                    ), cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Create Provider Service Provider")
            .WithSummary("Create a new service provider")
            .WithDescription("Creates a new service provider. The current user becomes the owner.")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });

        // Update service provider
        app.MapPut("/service-providers/{id:int}",
                async (IMediator mediator, [FromRoute] int id, UpdateServiceProviderRequest request,
                    CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new UpdateServiceProviderCommand(
                        id,
                        request.Name,
                        request.ServiceCategoryId,
                        request.StatusId,
                        request.Description,
                        request.Phone,
                        request.OwnerPhone,
                        request.Address,
                        request.CountryName,
                        request.CityName,
                        request.Latitude,
                        request.Longitude,
                        request.Price,
                        request.PriceDescription,
                        request.FromTime,
                        request.ToTime,
                        request.MethodPayment,
                        request.IsVerified,
                        request.HasOffer,
                        request.OfferDescription,
                        request.Service,
                        request.Note,
                        request.WhatsAppNumber,
                        request.WebsiteUrl
                    ), cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Update Provider Service Provider")
            .WithSummary("Update your own service provider")
            .WithDescription("Updates a service provider. Only the owner can update their own provider.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the service provider to update";
                op.RequestBody.Required = true;
                return op;
            });

        // Delete service provider
        app.MapDelete("/service-providers/{id:int}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new DeleteServiceProviderCommand(id), cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Delete Provider Service Provider")
            .WithSummary("Delete your own service provider")
            .WithDescription("Soft-deletes a service provider. Only the owner can delete their own provider.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the service provider to delete";
                return op;
            });

        // Upload service provider icon
        app.MapPost("/service-providers/{id:int}/upload-icon",
                async (IMediator mediator, [FromForm] IFormFile file, [FromRoute] int id,
                        CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new UploadServiceProviderIconCommand(file, id), cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Upload Provider Service Provider Icon")
            .WithSummary("Upload an icon for your service provider")
            .WithDescription("Uploads an icon image. Replaces the existing icon if one exists. Only the owner can upload.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the service provider";
                return op;
            })
            .DisableAntiforgery();

        // Add service provider attachments
        app.MapPost("/service-providers/{id:int}/add-attachments",
                async (IMediator mediator, [FromRoute] int id, IFormFileCollection files,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new AddServiceProviderAttachmentCommand(id, files),
                        cancellationToken)))
            .Produces<int[]>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Add Provider Service Provider Attachments")
            .WithSummary("Upload attachments for your service provider")
            .WithDescription("Uploads one or more files as attachments. Only the owner can upload.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the service provider";
                return op;
            })
            .DisableAntiforgery();

        // Delete service provider attachments
        app.MapDelete("/service-providers/{id:int}/delete-attachments",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new DeleteServiceProviderAttachmentCommand(id), cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Delete Provider Service Provider Attachments")
            .WithSummary("Delete all attachments for your service provider")
            .WithDescription("Deletes all attachments. Only the owner can delete.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the service provider";
                return op;
            });

        return app;
    }

    #endregion
}
