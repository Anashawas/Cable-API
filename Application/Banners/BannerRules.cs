using Application.Common.Localization;
using Cable.Core.Utilities;
using FluentValidation;

namespace Application.Banners;

/// <summary>
/// Banner action types (what tapping the banner does in the app) and the phone rules that go with them.
/// 1 open link · 2 WhatsApp · 3 station details · 4 maps · 5 contact us · 6 phone call (Oct 2026).
/// For 2 and 6, ActionUrl holds a phone number; empty means "use the banner's Phone".
/// </summary>
public static class BannerRules
{
    public const int OpenUrl = 1, WhatsApp = 2, StationDetails = 3, Maps = 4, ContactUs = 5, PhoneCall = 6;
    public const int MaxActionType = PhoneCall;

    public static bool IsKnownActionType(int? type) => type is null || (type >= OpenUrl && type <= MaxActionType);

    public static bool ActionIsPhone(int? type) => type is WhatsApp or PhoneCall;

    /// <summary>Storage form of the banner's own phone: normalised when dialable, else as typed (validation rejects that on save).</summary>
    public static string NormalizePhone(string? phone) =>
        string.IsNullOrWhiteSpace(phone) ? phone ?? "" : DialablePhone.Normalize(phone) ?? phone;

    /// <summary>Storage form of ActionUrl: for WhatsApp / call a normalised number, otherwise untouched.</summary>
    public static string? NormalizeActionUrl(int? type, string? actionUrl) =>
        ActionIsPhone(type) && !string.IsNullOrWhiteSpace(actionUrl) ? DialablePhone.Normalize(actionUrl) ?? actionUrl : actionUrl;

    /// <summary>
    /// API form of ActionUrl. A call (6) gets the dial-ready "+…" form. WhatsApp (2) keeps bare digits:
    /// wa.me links must not carry a "+", and app versions released before Oct 2026 build the link straight from this value.
    /// </summary>
    public static string? ActionUrlForDisplay(int? type, string? actionUrl) =>
        type == PhoneCall ? DialablePhone.ForDisplay(actionUrl) : actionUrl;

    public static string? PhoneForDisplay(string? phone) => DialablePhone.ForDisplay(phone);

    /// <summary>The shared validation for add and update.</summary>
    public static void AddActionRules<T>(AbstractValidator<T> v, Func<T, int?> type, Func<T, string?> actionUrl, Func<T, string?> phone)
    {
        v.RuleFor(x => type(x))
            .Must(IsKnownActionType)
            .OverridePropertyName("ActionType")
            .WithMessage(_ => Resources.InvalidBannerActionType);

        v.RuleFor(x => phone(x))
            .Must(p => string.IsNullOrWhiteSpace(p) || DialablePhone.IsValid(p))
            .OverridePropertyName("Phone")
            .WithMessage(_ => Resources.InvalidPhoneNumber);

        v.When(x => ActionIsPhone(type(x)), () =>
        {
            v.RuleFor(x => actionUrl(x))
                .Must(u => string.IsNullOrWhiteSpace(u) || DialablePhone.IsValid(u))
                .OverridePropertyName("ActionUrl")
                .WithMessage(_ => Resources.InvalidPhoneNumber);

            // The app falls back to Phone when ActionUrl is empty — one of the two must hold a number.
            v.RuleFor(x => x)
                .Must(x => !string.IsNullOrWhiteSpace(actionUrl(x)) || !string.IsNullOrWhiteSpace(phone(x)))
                .OverridePropertyName("ActionUrl")
                .WithMessage(_ => Resources.InvalidPhoneNumber);
        });
    }
}
