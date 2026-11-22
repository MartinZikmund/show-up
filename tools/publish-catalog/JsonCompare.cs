using System.Text.Json;

namespace PublishCatalog;

/// <summary>Structural JSON equality: key order, whitespace and number spelling (30 vs 30.0) don't matter, as with jsonb.</summary>
public static class JsonCompare
{
    // A duplicate key keeps the last value, like System.Text.Json and jsonb.
    private static Dictionary<string, JsonElement> Members(JsonElement e)
    {
        Dictionary<string, JsonElement> members = new(StringComparer.Ordinal);
        foreach (var property in e.EnumerateObject())
        {
            members[property.Name] = property.Value;
        }

        return members;
    }

    public static bool Equal(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind)
        {
            return false;
        }

        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
                var left = Members(a);
                var right = Members(b);
                return left.Count == right.Count
                    && left.All(p => right.TryGetValue(p.Key, out var other) && Equal(p.Value, other));
            case JsonValueKind.Array:
                var xs = a.EnumerateArray().ToList();
                var ys = b.EnumerateArray().ToList();
                return xs.Count == ys.Count && xs.Zip(ys).All(pair => Equal(pair.First, pair.Second));
            case JsonValueKind.String:
                return a.GetString() == b.GetString();
            case JsonValueKind.Number:
                return a.TryGetDecimal(out var da) && b.TryGetDecimal(out var db) ? da == db : a.GetDouble() == b.GetDouble();
            default:
                return true;
        }
    }
}
