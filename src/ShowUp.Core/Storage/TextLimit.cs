namespace ShowUp.Core.Storage;

internal static class TextLimit
{
    /// <summary>Cuts to at most <paramref name="maxLength"/> UTF-16 units without leaving half of a surrogate pair.</summary>
    public static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        // A lone high surrogate is not valid text: it turns into U+FFFD on the way to the server.
        int length = maxLength > 0 && char.IsHighSurrogate(text[maxLength - 1]) ? maxLength - 1 : maxLength;
        return text[..length];
    }
}
