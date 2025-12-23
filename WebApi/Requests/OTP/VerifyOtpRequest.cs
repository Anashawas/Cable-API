namespace Cable.Requests.OTP;

public record VerifyOtpRequest(string PhoneNumber, string OtpCode);
