using System.Reflection;
using Application.Common.Interfaces;
using Application.Common.Models;
using Cable.Core;
using MediatR;
using Microsoft.Extensions.Hosting;

namespace Application.Common.Behaviours;

internal class AuthorizationBehaviour<TRequest, TResponse>(
    ICurrentUserService currentUserService,
    IIdentityService identityService,
    IHostEnvironment environment)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var authorizeAttributes = request.GetType().GetCustomAttributes<ApplicationAuthorizeAttribute>();

        // //TODO: remove when users table is ready
        // if (!environment.IsDevelopment())
        // {
            if (authorizeAttributes.Any())
            {
                if (!currentUserService.UserId.HasValue)
                {
                    throw new NotAuthorizedAccessException();
                }

                var privlegesAttributes = authorizeAttributes.Where(a => !string.IsNullOrEmpty(a.PrivilegeCode));


                if (privlegesAttributes.Any())
                {

                    var privileges = privlegesAttributes.Select(x => x.PrivilegeCode).Distinct();

                    if (!privileges.Any(x => String.IsNullOrEmpty(x)))
                    {
                        bool authorized = false;

                        foreach (var privilege in privileges)
                        {

                            if (await identityService.HasPrivilege(currentUserService.UserId.Value, privilege))
                            {
                                authorized = true;
                                break;
                            }
                        }

                        if (!authorized)
                        {
                            throw new ForbiddenAccessException();
                        }

                    }

                }
            }
        // }

        return await next();
    }
}
