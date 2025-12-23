using Application.Common.Interfaces;

namespace Application.Authentication.Commands.SendOtp;

public record SendOtpCommand(string PhoneNumber) : IRequest<SendOtpResult>;

public class SendOtpCommandHandler : IRequestHandler<SendOtpCommand, SendOtpResult>
{
    private readonly IOtpService _otpService;

    public SendOtpCommandHandler(IOtpService otpService)
    {
        _otpService = otpService;
    }

    public async Task<SendOtpResult> Handle(SendOtpCommand request, CancellationToken cancellationToken)
    {
        if (await _otpService.IsRateLimitedAsync(request.PhoneNumber, cancellationToken))
        {
            return new SendOtpResult(false, "Rate limit exceeded. Please try again later.", null);
        }
        
        var otp = await _otpService.GenerateOtpAsync(request.PhoneNumber, cancellationToken);
        var sent = await _otpService.SendOtpAsync(request.PhoneNumber, otp, cancellationToken);

        if (sent)
        {
            return new SendOtpResult(true, "OTP sent successfully", DateTime.Now.AddMinutes(5));
        }

        return new SendOtpResult(false, "Failed to send OTP", null);
    }
}