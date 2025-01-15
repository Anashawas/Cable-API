namespace Infrastructrue.Utilities;

public static class UrlHelper
{
    public static string AppendEndSlash(string url) => url.EndsWith("/") ? url : $"{url}/";
}