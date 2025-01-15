using System.Security.Authentication;

namespace Infrastructrue.Utilities;

public static class SslProtocolsHelper
{
    public static SslProtocols FromArray(SslProtocols[] sslProtocols)
    {
        if (sslProtocols == null || sslProtocols.Length == 0)
        {
            return SslProtocols.None;
        }

        if (sslProtocols.Length == 1)
        {
            return sslProtocols[0];
        }

        var output = sslProtocols[0];

        for (int i = 1; i <= sslProtocols.Length; i++)
        {
            output |= sslProtocols[i];
        }

        return output;
    }
}
