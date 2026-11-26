using TechDigest.Models;

namespace TechDigest.Services.Ranking;

/// <summary>
/// Kümelere konu atar, önem puanı verir ve bültenin son hâlini seçer.
///
/// Önem puanı = konu uygunluğu (0–9)
///            + kapsama: 4 × log2(yayıncı sayısı)   → 1 kaynak 0, 2 kaynak 4, 4 kaynak 8, 8 kaynak 12
///            + kaynak kalitesi: Top +4, Trusted +2, Normal 0, Low −3
///            + tazelik: son 6 saat +2, son 12 saat +1
/// Böylece anahtar kelimeyle doldurulmuş tek kaynaklı bir haber, Reuters ve Bloomberg'in birlikte
/// verdiği bir haberin önüne geçemez.
/// </summary>
public sealed class StoryRanker(TopicConfig config, PublisherDirectory publishers)
{
    private readonly TopicMatcher _matcher = new(config.Topics);

    /// <summary>Konu atar ve puanlar. Konusu olmayan kümeler elenir. Sonuç puana göre sıralıdır.</summary>
    public List<Story> Rank(IEnumerable<Story> stories, DateTimeOffset now)
    {
        var ranked = new List<Story>();
        foreach (var story in stories)
        {
            // Konu, kümedeki en uygun haberden gelir (lider kısa başlıklı olabilir).
            var (topic, relevance) = story.All
                .Select(_matcher.Match)
                .OrderByDescending(m => m.Relevance)
                .First();
            if (topic is null) continue;

            story.Topic = topic;
            story.Relevance = relevance;
            story.Score = Score(story, now);
            ranked.Add(story);
        }
        return ranked.OrderByDescending(s => s.Score).ToList();
    }

    public double Score(Story story, DateTimeOffset now)
    {
        var coverage = 4 * Math.Log2(Math.Max(1, story.Coverage));
        var quality = publishers.TierOf(story.Lead.Source) switch
        {
            PublisherTier.Top => 4,
            PublisherTier.Trusted => 2,
            PublisherTier.Low => -3,
            _ => 0,
        };
        var age = now - story.Latest;
        var freshness = age < TimeSpan.FromHours(6) ? 2 : age < TimeSpan.FromHours(12) ? 1 : 0;
        return Math.Round(story.Relevance + coverage + quality + freshness, 2);
    }

    /// <summary>
    /// Gemini'ye gidecek adaylar: her konudan en iyi birkaç küme. Son seçimden geniştir ki
    /// Gemini anlamsal kopyaları birleştirdiğinde yerlerine geçecek haber olsun.
    /// </summary>
    public List<Story> Candidates(IReadOnlyList<Story> ranked) =>
        ranked
            .GroupBy(s => s.Topic!.Id)
            .SelectMany(g => g.Take(config.CandidatesPerTopic))
            .OrderByDescending(s => s.Score)
            .Take(config.MaxCandidates)
            .ToList();

    /// <summary>
    /// Bültenin son hâli: en yüksek puanlı küme manşet olur, kalanlar konu başına en fazla
    /// <c>MaxPerTopic</c>, toplamda <c>MaxTotal</c> haber. Bölüm sırası topics.json'daki sırayla aynıdır
    /// (okuyucu her gün aynı düzeni görür).
    /// </summary>
    public (Story? Headline, List<DigestSection> Sections) Compose(IReadOnlyList<Story> ranked)
    {
        var ordered = ranked.OrderByDescending(s => s.Score).ToList();
        var headline = ordered.FirstOrDefault();

        var perTopic = ordered.Skip(1)
            .GroupBy(s => s.Topic!.Id)
            .ToDictionary(g => g.Key, g => g.Take(config.MaxPerTopic).ToList());

        var keep = perTopic.Values.SelectMany(s => s)
            .OrderByDescending(s => s.Score)
            .Take(Math.Max(0, config.MaxTotal - 1))
            .ToHashSet();

        var sections = config.Topics
            .Select(t => new DigestSection(t, perTopic.GetValueOrDefault(t.Id, []).Where(keep.Contains).ToList()))
            .Where(s => s.Stories.Count > 0)
            .ToList();

        return (headline, sections);
    }
}
