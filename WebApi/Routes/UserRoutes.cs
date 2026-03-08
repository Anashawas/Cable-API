using Application.Authentication.Commands.SendOtp;
using Application.Authentication.Commands.VerifyOtp;
using Application.Common.Interfaces;
using Application.Common.Models;
using Application.Users.Commands.AddUser;
using Application.Users.Commands.ChangePassword;
using Application.Users.Commands.DeleteUser;
using Application.Users.Commands.UpdateUser;
using Application.Users.Commands.RequestPasswordReset;
using Application.Users.Commands.ResetPasswordWithCode;
using Application.Users.Commands.ValidateResetCode;
using Application.Users.Commands.VerifyPhone.SendPhoneVerificationOtp;
using Application.Users.Commands.VerifyPhone.VerifyUserPhone;
using Application.Users.Queries.GetAllUsers;
using Application.Users.Queries.GetUserById;
using Cable.Requests.Identity;
using Cable.Requests.OTP;
using Cable.Requests.Users;
using Cable.Responses.Identity;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class UserRoutes
{
    public static IEndpointRouteBuilder MapUserRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/users")
            .WithTags("Users")
            .MapAuthenticationRoutes()
            .MapAdministrationRoutes();

        return app;
    }

    private static RouteGroupBuilder MapAdministrationRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/GetAllUsers", async (IMediator mediator, CancellationToken cancellation) =>
                Results.Ok(await mediator.Send(new GetAllUsersRequest(), cancellation)))
            .RequireAuthorization()
            .Produces<List<GetAllUsersDto>>()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("List Users")
            .WithSummary("Lists all users")
            .WithOpenApi();

        app.MapGet("/GetUserById/{id:int}",
            async ([FromRoute] int id, IMediator mediator, CancellationToken cancellationToken)
                => Results.Ok(await mediator.Send(new GetUserByIdRequest(id), cancellationToken)))
            .RequireAuthorization()
            .Produces<GetUserByIdDto>()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Get User ById")
            .WithSummary("Get User ById")
            .WithOpenApi();
            ; 

        app.MapPost("/AddUser", async (IMediator mediator, AddUserCommand request, CancellationToken cancellation) =>
                Results.Ok(await mediator.Send(request, cancellation)))
            .Produces<UserDetailsResult>()
            .ProducesInternalServerError()
            .WithName("Add User")
            .WithSummary("Adds a new user without phone. Phone must be added via verify-phone endpoints.")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                op.Responses["200"].Description = "The id of the user";
                return op;
            });
   
   

        app.MapPut("/{id}",
                async (IMediator mediator, [FromRoute] int id, UpdateUserRequest request,
                        CancellationToken cancellation) =>
                    await mediator.Send(new
                        UpdateUserCommand(id, request.Name, request.RoleId, request.Email, request.IsActive, request.Country, request.City), cancellation))
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Update User")
            .WithSummary("Updates an existing user. Phone is managed separately via verify-phone endpoints.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The id of the user";
                op.RequestBody.Required = true;
                return op;
            });
        ;

        app.MapPatch("/{id}/change-password", async (IMediator mediator, [FromRoute] int id,
                    ChangePasswordRequest request, CancellationToken cancellation) =>
                await mediator.Send(new ChangePasswordCommand(id, request.Password), cancellation))
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Change User Password")
            .WithSummary("Changes the password of an existing user")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The id of the user";
                op.RequestBody.Required = true;
                return op;
            });
        ;


        app.MapPatch("/change-my-password", async (IMediator mediator,
                    ChangeMyPasswordCommand request, CancellationToken cancellation) =>
                await mediator.Send(request, cancellation))
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Change My Password")
            .WithSummary("Changes the logged in user password")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });
        ;

        app.MapPost("/request-password-reset", async (IMediator mediator,
                    RequestPasswordResetRequest request, CancellationToken cancellation) =>
                Results.Ok(await mediator.Send(new RequestPasswordResetCommand(request.Email), cancellation)))
            .Produces<RequestPasswordResetDto>()
            .ProducesValidationProblem()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Request Password Reset")
            .WithSummary("Sends a 6-digit reset code to the user's email")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });
        ;

        app.MapPost("/validate-reset-code", async (IMediator mediator,
                    ValidateResetCodeRequest request, CancellationToken cancellation) =>
                Results.Ok(await mediator.Send(new ValidateResetCodeCommand(request.Email, request.Code), cancellation)))
            .Produces<ValidateResetCodeDto>()
            .ProducesValidationProblem()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Validate Reset Code")
            .WithSummary("Validates the 6-digit reset code before changing password")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });
        ;

        app.MapPost("/reset-password", async (IMediator mediator,
                    ResetPasswordWithCodeRequest request, CancellationToken cancellation) =>
                Results.Ok(await mediator.Send(new ResetPasswordWithCodeCommand(request.Email, request.Code, request.NewPassword), cancellation)))
            .Produces<ResetPasswordWithCodeDto>()
            .ProducesValidationProblem()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Reset Password With Code")
            .WithSummary("Resets the user's password using the 6-digit code sent via email")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });
        ;

        app.MapPost("/verify-phone/send-otp", async (IMediator mediator,
                    SendPhoneVerificationOtpRequest request, CancellationToken cancellation) =>
                Results.Ok(await mediator.Send(new SendPhoneVerificationOtpCommand(request.PhoneNumber), cancellation)))
            .RequireAuthorization()
            .Produces<SendPhoneVerificationOtpDto>()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesValidationProblem()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Send Phone Verification OTP")
            .WithSummary("Sends OTP to verify and link phone number to current user's account")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });
        ;

        app.MapPost("/verify-phone/verify-otp", async (IMediator mediator,
                    VerifyUserPhoneRequest request, CancellationToken cancellation) =>
                Results.Ok(await mediator.Send(new VerifyUserPhoneCommand(request.PhoneNumber, request.OtpCode), cancellation)))
            .RequireAuthorization()
            .Produces<VerifyUserPhoneDto>()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesValidationProblem()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Verify User Phone")
            .WithSummary("Verifies OTP and links phone number to current user's account")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });
        ;


        app.MapDelete("/{id:int}", async (int id, IMediator mediator, CancellationToken cancellation) =>
                await mediator.Send(new DeleteUserCommand(id), cancellation))
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Delete User")
            .WithSummary("Deletes an existing user")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The id of the user";
                return op;
            });
        ;

        return app;
    }

    private static RouteGroupBuilder MapAuthenticationRoutes(this RouteGroupBuilder app)
    {
        app.MapPost("/authenticate", async (LoginRequest request, IAuthenticationService authenticationService,
                    CancellationToken cancellationToken)
                => Results.Ok(await authenticationService.Login(request.Email, request.Password, cancellationToken))
            )
            .AddEndpointFilter<LoginRequestValidationFilter>()
            .ProducesInternalServerError()
            .Produces<UserLoginResult>()
            .WithName("Authenticates User")
            .WithSummary("Authenticates a user based on the provided login details")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });
        

             
        app.MapPost("/login-by-google", async ( IAuthenticationService authenticationService,FirebaseLoginDetails firebaseLoginDetails, CancellationToken cancellationToken) =>
                Results.Ok(await authenticationService.LoginFirebaseAsync(firebaseLoginDetails, cancellationToken)))
            .Produces<UserLoginResult>()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Login by google token ")
            .WithSummary("Login by google token for user")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });
        
        
        app.MapPost("/login-by-token", async (ICurrentUserService currentUserService,
                    IAuthenticationService authenticationService, CancellationToken cancellationToken)
                => Results.Ok(await authenticationService.LoginByToken(currentUserService.Token, cancellationToken))
            ).RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .Produces<UserLoginByTokenResult>()
            .WithName("Authenticate By Token")
            .WithSummary("Authenticate a user by their authorization header token")
            .WithOpenApi();

        app.MapPost("/refresh-access", async (RefreshTokenRequest refreshTokenRequest,
                    IAuthenticationService authenticationService, CancellationToken cancellationToken)
                =>
            {
                var (accessToken, refreshToken) =
                    await authenticationService.RefreshTokens(refreshTokenRequest.Token);
                return Results.Ok(new UserToken(accessToken, refreshToken));
            })
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .Produces<UserToken>()
            .WithName("Refresh User Access")
            .WithSummary("Refreshes the user access and refresh tokens")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });

        app.MapPost("/send-otp", async (IAuthenticationService authenticationService, SendOtpRequest request, IMediator mediator, CancellationToken cancellationToken) =>
                Results.Ok(await authenticationService.SendOtpAsync(request.PhoneNumber, cancellationToken)))
            .Produces<SendOtpResult>()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Send OTP")
            .WithSummary("Send OTP to phone number for authentication")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });

        app.MapPost("/verify-otp", async (IAuthenticationService authenticationService,VerifyOtpRequest request, IMediator mediator, CancellationToken cancellationToken) =>
                Results.Ok(await authenticationService.LoginWithOtpAsync(request.PhoneNumber, request.OtpCode, cancellationToken)))
            .Produces<UserLoginResult>()
            .ProducesValidationProblem()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Verify OTP")
            .WithSummary("Verify OTP and authenticate user")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
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
            .WithName("Logout")
            .WithSummary("Logs out the user by invalidating all active sessions")
            .WithOpenApi();

        return app;
    }
}