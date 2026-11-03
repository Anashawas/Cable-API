using Cable.Core.Utilities;

namespace Application.Workers.WorkerAuth;

/// <summary>
/// The ready-to-send message of the "share with the worker" screen (§3.2.2). Built on the server so the app, the
/// web portal and the printed card show exactly the same text.
/// </summary>
public static class WorkerShareMessage
{
    public static string Build(string? language, string workerName, string ownerName, string providerName,
        string phoneLocal, string tempPassword, DateTime expiresAtUtc, int validHours, string loginUrl, bool hasBiometricLogin)
    {
        var expires = JordanTime.FromUtc(expiresAtUtc);
        var expiresText = expires.ToString("yyyy-MM-dd HH:mm");

        if (!string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
        {
            var ar =
                $"أهلاً {workerName} 👋\n" +
                $"ضافك {ownerName} كموظف بـ«{providerName}» على تطبيق كيبل بارتنر.\n\n" +
                $"١. افتح تطبيق كيبل بارتنر، أو ادخل من هون: {loginUrl}\n" +
                $"٢. ادخل برقم موبايلك: {phoneLocal}\n" +
                $"٣. كلمة السر المؤقتة: {tempPassword}\n" +
                $"   (صالحة لـ{validHours} ساعة — لحد {expiresText} — ورح يُطلب منك تغييرها أول مرة)\n";
            if (hasBiometricLogin) ar += "\nبعدها بتقدر تدخل بالبصمة.\n";
            return ar;
        }

        var en =
            $"Hi {workerName} 👋\n" +
            $"{ownerName} added you as a worker at \"{providerName}\" on the Cable Partner app.\n\n" +
            $"1. Open the Cable Partner app, or sign in here: {loginUrl}\n" +
            $"2. Sign in with your phone number: {phoneLocal}\n" +
            $"3. Temporary password: {tempPassword}\n" +
            $"   (valid for {validHours} hours — until {expiresText} — you will be asked to change it the first time)\n";
        if (hasBiometricLogin) en += "\nAfter that you can sign in with your fingerprint or face.\n";
        return en;
    }
}
