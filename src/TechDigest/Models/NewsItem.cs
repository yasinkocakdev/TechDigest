namespace TechDigest.Models;

/// <summary>
/// Tek bir haber. RSS'ten okunur, sonraki adımlarda konu ve Türkçe özet eklenir.
/// </summary>
public sealed record NewsItem(
    string Title,
    string Summary,
    string Link,
    string Source,
    DateTimeOffset Published);

/// <summary>sources.json içindeki bir satır.</summary>
public sealed record FeedSource(string Name, string Url);
