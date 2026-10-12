namespace AtmCash.Application.Common;

/// <summary>Daily limits reset at midnight Istanbul time.</summary>
public static class BusinessDay
{
    private static readonly TimeZoneInfo Zone = ResolveZone();

    public static DateTimeOffset StartOfDay(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, Zone);
        var midnight = new DateTime(local.Year, local.Month, local.Day, 0, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(midnight, Zone.GetUtcOffset(midnight)).ToUniversalTime();
    }

    private static TimeZoneInfo ResolveZone()
    {
        foreach (var id in new[] { "Europe/Istanbul", "Turkey Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
            {
                return zone;
            }
        }

        // Turkey has been on fixed UTC+3 since 2016.
        return TimeZoneInfo.CreateCustomTimeZone("TRT", TimeSpan.FromHours(3), "Turkey Time", "Turkey Time");
    }
}
