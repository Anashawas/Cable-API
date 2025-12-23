using Application.Common.Interfaces;
using Application.Common.Models;

namespace Application.Authentication.Commands.VerifyOtp;

public record VerifyOtpCommand(string PhoneNumber, string OtpCode) : IRequest<UserLoginResult>;

public class VerifyOtpCommandHandler : IRequestHandler<VerifyOtpCommand, UserLoginResult>
{
    private readonly IAuthenticationService _authenticationService;

    public VerifyOtpCommandHandler(IAuthenticationService authenticationService)
    {
        _authenticationService = authenticationService;
    }

    public async Task<UserLoginResult> Handle(VerifyOtpCommand request, CancellationToken cancellationToken)
    {
        return await _authenticationService.LoginWithOtpAsync(request.PhoneNumber, request.OtpCode, cancellationToken);
    }
}