using System.Text.RegularExpressions;
using TechDigest.Models;

namespace TechDigest.Services;

/// <summary>
/// Her haberi anahtar kelimelere göre puanlar ve en yüksek puanlı konuya atar.
/// Başlıktaki eşleşme 3 puan, açıklamadaki 1 puan. Hiçbir konuya uymayan haber elenir.
/// </summary>
public sealed class Categorizer(TopicConfig config)
{
    private const int TitleWeight = 3;
    private const int SummaryWeight = 1;

    public List<DigestSection> Categorize(IEnumerable<NewsItem> items)
    {
        var buckets = config.Topics.ToDictionary(t => t, _ => new List<DigestItem>());

        foreach (var item in items)
        {
            if (IsExcluded(item)) continue;

            var best = config.Topics
                .Select(topic => (topic, score: Score(item, topic)))
                .MaxBy(x => x.score);

            if (best.score > 0)
                buckets[best.topic].Add(new DigestItem(item, best.score));
        }

        // Her konudan en iyi N haber; sonra toplam sınırı aşmamak için en zayıflar kırpılır.
        var sections = config.Topics
            .Select(t => new DigestSection(t, buckets[t]
                .OrderByDescending(d => d.Score)
                .ThenByDescending(d => d.News.Published)
                .Take(config.MaxPerTopic)
                .ToList()))
            .ToList();

        var keep = sections.SelectMany(s => s.Items)
            .OrderByDescending(d => d.Score)
            .Take(config.MaxTotal)
            .ToHashSet();

        return sections
            .Select(s => s with { Items = s.Items.Where(keep.Contains).ToList() })
            .Where(s => s.Items.Count > 0)
            .ToList();
    }

    private bool IsExcluded(NewsItem item)
    {
        var text = $"{item.Title} ({item.Source})".ToLowerInvariant();
        return config.ExcludeKeywords.Any(k => text.Contains(k.ToLowerInvariant()));
    }

    public static int Score(NewsItem item, Topic topic)
    {
        var title = item.Title.ToLowerInvariant();
        var summary = item.Summary.ToLowerInvariant();
        var score = 0;
        foreach (var keyword in topic.Keywords)
        {
            if (ContainsWord(title, keyword)) score += TitleWeight;
            else if (ContainsWord(summary, keyword)) score += SummaryWeight;
        }
        return score;
    }

    /// <summary>"ai" kelimesi "said" içinde eşleşmesin diye kelime sınırına bakar.</summary>
    private static bool ContainsWord(string text, string keyword) =>
        Regex.IsMatch(text, $@"(?<![\w]){Regex.Escape(keyword.ToLowerInvariant())}(?![\w])");
}
