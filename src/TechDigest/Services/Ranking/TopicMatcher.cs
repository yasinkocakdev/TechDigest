using System.Text.RegularExpressions;
using TechDigest.Models;

namespace TechDigest.Services.Ranking;

/// <summary>
/// Bir haberi konulara puanlar. Her konu için tek bir regex bir kez derlenir (eski sürüm her
/// karşılaştırmada yeniden derliyordu). Kelime sınırına bakılır: "ai" kelimesi "said" içinde eşleşmez.
/// </summary>
public sealed class TopicMatcher
{
    public const int StrongTitle = 3, StrongSummary = 1, WeakTitle = 1, MaxRelevance = 9;

    private readonly List<(Topic Topic, Regex Strong, Regex? Weak)> _topics;

    public TopicMatcher(IEnumerable<Topic> topics) =>
        _topics = topics.Select(t => (t, Build(t.Strong) ?? Never, Build(t.Weak))).ToList();

    private static readonly Regex Never = new("(?!)", RegexOptions.Compiled);

    private static Regex? Build(IReadOnlyCollection<string> keywords) => keywords.Count == 0
        ? null
        : new Regex(
            $@"(?<![\p{{L}}\p{{N}}])(?:{string.Join("|", keywords.OrderByDescending(k => k.Length).Select(Regex.Escape))})(?![\p{{L}}\p{{N}}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// En uygun konuyu ve uygunluk puanını döner. Konu atanması için başlıkta ya da açıklamada en az bir
    /// "güçlü" kelime gerekir. Her kelime bir kez sayılır. Eşitlikte topics.json'daki sıra kazanır.
    /// </summary>
    public (Topic? Topic, int Relevance) Match(NewsItem item)
    {
        Topic? best = null;
        var bestScore = 0;
        foreach (var (topic, strong, weak) in _topics)
        {
            var inTitle = Hits(strong, item.Title);
            var inSummaryOnly = Hits(strong, item.Summary);
            inSummaryOnly.ExceptWith(inTitle);
            if (inTitle.Count == 0 && inSummaryOnly.Count == 0) continue;

            var score = inTitle.Count * StrongTitle
                        + inSummaryOnly.Count * StrongSummary
                        + (weak is null ? 0 : Hits(weak, item.Title).Count * WeakTitle);
            if (score > bestScore) (best, bestScore) = (topic, score);
        }
        return (best, Math.Min(bestScore, MaxRelevance));
    }

    private static HashSet<string> Hits(Regex regex, string text) =>
        text.Length == 0 ? [] : regex.Matches(text).Select(m => m.Value.ToLowerInvariant()).ToHashSet();
}
