using System.Text.RegularExpressions;
using TechDigest.Models;

namespace TechDigest.Services;

/// <summary>
/// Aynı haber birden fazla kaynaktan gelirse (ör. Reuters ve CNN aynı Nvidia haberini verir)
/// sadece ilkini tutar. Başlıklar kelime kümelerine çevrilip Jaccard benzerliğiyle karşılaştırılır.
/// </summary>
public static partial class Deduplicator
{
    public const double DefaultThreshold = 0.6;

    private static readonly HashSet<string> StopWords =
        ["the", "a", "an", "of", "to", "in", "on", "for", "and", "or", "is", "as", "at", "by", "with", "after", "its", "it", "from"];

    /// <summary>Liste sırası önemli: önce gelen (daha yeni) haber kalır.</summary>
    public static List<NewsItem> RemoveDuplicates(IEnumerable<NewsItem> items, double threshold = DefaultThreshold)
    {
        var kept = new List<(NewsItem item, HashSet<string> words)>();
        foreach (var item in items)
        {
            var words = Words(item.Title);
            if (words.Count == 0) continue;
            if (kept.Any(k => k.item.Link == item.Link || IsSameStory(k.words, words, threshold))) continue;
            kept.Add((item, words));
        }
        return kept.Select(k => k.item).ToList();
    }

    /// <summary>
    /// İki kural: başlıklar genel olarak çok benziyorsa (Jaccard) ya da kısa başlığın
    /// kelimelerinin çoğu uzun başlıkta geçiyorsa (en az 4 ortak kelimeyle) aynı haberdir.
    /// </summary>
    public static bool IsSameStory(HashSet<string> a, HashSet<string> b, double threshold = DefaultThreshold)
    {
        if (Jaccard(a, b) >= threshold) return true;
        var shared = a.Count(b.Contains);
        return shared >= 4 && (double)shared / Math.Min(a.Count, b.Count) >= 0.6;
    }

    public static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 && b.Count == 0) return 1;
        var intersection = a.Count(b.Contains);
        return (double)intersection / (a.Count + b.Count - intersection);
    }

    public static HashSet<string> Words(string title)
    {
        // Google News başlıkları "... - Reuters" diye bitiyor; kaynak adını at.
        var withoutSource = SourceSuffix().Replace(title, "");
        return WordPattern().Matches(withoutSource.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(w => !StopWords.Contains(w))
            .Select(Stem)
            .ToHashSet();
    }

    /// <summary>Çok kaba kök bulma: "exports" ve "export" aynı sayılsın.</summary>
    private static string Stem(string w) => w.Length > 4 && w.EndsWith('s') && !w.EndsWith("ss") ? w[..^1] : w;

    [GeneratedRegex(@"\s+[-–|]\s+[^-–|]+$")] private static partial Regex SourceSuffix();
    [GeneratedRegex(@"[\p{L}\p{N}]+")] private static partial Regex WordPattern();
}
