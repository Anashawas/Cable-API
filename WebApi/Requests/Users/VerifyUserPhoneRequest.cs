namespace Cable.Requests.Users;

public record VerifyUserPhoneRequest(string PhoneNumber, string OtpCode);
