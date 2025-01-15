namespace Cable.Core.Extenstions;

public static class Converters
{
    public static int? AsInt(this string s)
    {
        if (int.TryParse(s, out int result))
        {
            return result;
        }
        return null;
    }
}
