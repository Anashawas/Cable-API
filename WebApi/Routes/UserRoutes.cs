using Application.Common.Interfaces;
using Application.Common.Models;
using Application.Users.Commands.AddUser;
using Application.Users.Commands.ChangePassword;
using Application.Users.Commands.DeleteUser;
using Application.Users.Commands.UpdateUser;
using Application.Users.Queries.GetAllUsers;
using Application.Users.Queries.GetUserById;
using Cable.Requests.Identity;
using Cable.Requests.Users;
using Cable.Responses.Identity;
using Google.Apis.Auth;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Client.Internal;

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
            .Produces<int>()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Add User")
            .WithSummary("Adds a new user")
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
                        UpdateUserCommand(id, request.Name, request.UserName,
                            request.RoleId, request.Phone, request.Email,request.IsActive,request.Country, request.City), cancellation))
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Update User")
            .WithSummary("Updates an existing user")
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


        app.MapDelete("/{id}", async (int id, IMediator mediator, CancellationToken cancellation) =>
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
        ;

        return app;
    }
}