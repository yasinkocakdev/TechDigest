namespace TechDigest.Services.Rendering;

/// <summary>"3h ago" gibi göreli zaman. "13:07 UTC" okuyucuya bir şey söylemiyordu.</summary>
public static class TimeText
{
    public static string Ago(DateTimeOffset published, DateTimeOffset now)
    {
        var age = now - published;
        if (age < TimeSpan.FromMinutes(1)) return "just now";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes}m ago";
        if (age < TimeSpan.FromDays(1)) return $"{(int)age.TotalHours}h ago";
        return $"{(int)age.TotalDays}d ago";
    }
}
