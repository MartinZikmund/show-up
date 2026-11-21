namespace ShowUp.Core.Storage;

/// <summary>Row timestamps in whole microseconds (what Postgres keeps), so a row reads back exactly as written.</summary>
public static class Timestamps
{
    public static readonly TimeSpan Microsecond = TimeSpan.FromTicks(10);

    public static DateTimeOffset Truncate(DateTimeOffset value) => new(value.UtcTicks - value.UtcTicks % 10, TimeSpan.Zero);

    /// <summary>The next stamp for a row: <paramref name="now"/>, but always after <paramref name="previous"/>, even when the clock stepped back.</summary>
    public static DateTimeOffset Next(DateTimeOffset now, DateTimeOffset previous)
    {
        DateTimeOffset after = Truncate(previous) + Microsecond;
        DateTimeOffset truncated = Truncate(now);
        return truncated > after ? truncated : after;
    }
}
