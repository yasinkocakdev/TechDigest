using TechDigest.Models;

namespace TechDigest.Services.Ranking;

/// <summary>
/// Bülten için uygun olmayan haberleri eler: engelli yayıncılar (içerik çiftlikleri, "hangi hisseyi al"
/// siteleri) ve başlığında tık tuzağı / yatırım tavsiyesi kalıbı geçen haberler.
/// </summary>
public sealed class ContentFilter(TopicConfig config, PublisherDirectory publishers)
{
    private readonly string[] _patterns = config.ExcludePatterns.Select(p => p.ToLowerInvariant()).ToArray();

    public bool Allows(NewsItem item)
    {
        if (publishers.IsBlocked(item.Source)) return false;
        var title = item.Title.ToLowerInvariant();
        return !_patterns.Any(title.Contains);
    }
}
