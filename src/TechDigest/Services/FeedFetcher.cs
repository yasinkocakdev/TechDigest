using System.Net;
using System.ServiceModel.Syndication;
using System.Text.RegularExpressions;
using System.Xml;
using TechDigest.Models;

namespace TechDigest.Services;

/// <summary>
/// RSS/Atom kaynaklarını paralel çeker ve son N saatteki haberleri döner.
/// Bir kaynak çökerse diğerleri etkilenmez: hata loglanır, o kaynak boş sayılır.
/// </summary>
public sealed partial class FeedFetcher(HttpClient http)
{
    public async Task<List<NewsItem>> FetchAllAsync(
        IEnumerable<FeedSource> sources, TimeSpan maxAge, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow - maxAge;
        var tasks = sources.Select(s => FetchOneAsync(s, ct));
        var results = await Task.WhenAll(tasks);

        return results
            .SelectMany(items => items)
            .Where(i => i.Published >= cutoff)
            .OrderByDescending(i => i.Published)
            .ToList();
    }

    private async Task<List<NewsItem>> FetchOneAsync(FeedSource source, CancellationToken ct)
    {
        try
        {
            await using var stream = await http.GetStreamAsync(source.Url, ct);
            var items = Parse(stream, source.Name);
            Console.WriteLine($"  ✓ {source.Name}: {items.Count} haber");
            return items;
        }
        catch (Exception ex) when (ex is HttpRequestException or XmlException or TaskCanceledException)
        {
            Console.WriteLine($"  ✗ {source.Name}: {ex.Message}");
            return [];
        }
    }

    /// <summary>Ağdan bağımsız olduğu için testlerde doğrudan çağrılabilir.</summary>
    public static List<NewsItem> Parse(Stream xml, string sourceName)
    {
        using var reader = XmlReader.Create(xml, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
        var feed = SyndicationFeed.Load(reader);

        return feed.Items.Select(item =>
            {
                var (title, source) = SplitPublisher(Clean(item.Title?.Text), sourceName);
                var summary = Clean(item.Summary?.Text);
                // Google News açıklaması sadece başlığın tekrarı; işe yaramıyor.
                if (summary.StartsWith(title, StringComparison.OrdinalIgnoreCase)) summary = "";
                return new NewsItem(
                    Title: title,
                    Summary: summary,
                    Link: item.Links.FirstOrDefault()?.Uri.ToString() ?? "",
                    Source: source,
                    // Bazı kaynaklar sadece LastUpdatedTime doldurur.
                    Published: item.PublishDate != default ? item.PublishDate : item.LastUpdatedTime);
            })
            .Where(i => i.Title.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Google News başlıkları "Nvidia hits record - Reuters" şeklinde. Asıl yayıncıyı
    /// kaynak olarak gösterir, başlıktan ayırırız. Diğer kaynaklarda dokunmayız.
    /// </summary>
    public static (string Title, string Source) SplitPublisher(string title, string sourceName)
    {
        if (!sourceName.StartsWith("Google News", StringComparison.OrdinalIgnoreCase)) return (title, sourceName);
        var idx = title.LastIndexOf(" - ", StringComparison.Ordinal);
        return idx > 0 ? (title[..idx].Trim(), title[(idx + 3)..].Trim()) : (title, sourceName);
    }

    /// <summary>HTML etiketlerini ve &amp;amp; gibi kodlamaları temizler.</summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var noTags = HtmlTag().Replace(text, " ");
        return Whitespace().Replace(WebUtility.HtmlDecode(noTags), " ").Trim();
    }

    [GeneratedRegex("<[^>]+>")] private static partial Regex HtmlTag();
    [GeneratedRegex(@"\s+")] private static partial Regex Whitespace();
}
