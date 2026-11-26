namespace TechDigest.Models;

/// <summary>
/// RSS'ten okunan tek bir haber. <see cref="Source"/> haberi yayınlayan kuruluştur
/// (Google News için başlığın sonundaki yayıncı: "… - Reuters").
/// </summary>
public sealed record NewsItem(
    string Title,
    string Summary,
    string Link,
    string Source,
    DateTimeOffset Published);

/// <summary>sources.json içindeki bir satır. Publisher boşsa yayıncı başlıktan çıkarılır (Google News).</summary>
public sealed record FeedSource(string Name, string Url, string? Publisher = null)
{
    public bool IsAggregator => string.IsNullOrWhiteSpace(Publisher);
}
