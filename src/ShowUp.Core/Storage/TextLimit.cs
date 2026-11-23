using System.Globalization;

namespace ShowUp.Core.Storage;

internal static class TextLimit
{
    /// <summary>
    /// Cuts to at most <paramref name="maxLength"/> UTF-16 units on a text-element boundary: never half a surrogate
    /// pair (invalid text, U+FFFD on the way to the server), and never half of a joined emoji or a flag.
    /// </summary>
    public static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        int length = 0;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            int end = elements.ElementIndex + elements.GetTextElement().Length;
            if (end > maxLength)
            {
                break;
            }

            length = end;
        }

        return text[..length];
    }
}
