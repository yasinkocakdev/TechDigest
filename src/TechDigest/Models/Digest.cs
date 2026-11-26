namespace TechDigest.Models;

/// <summary>
/// Aynı olayı anlatan haberlerin kümesi. <see cref="Lead"/> en iyi kaynaktan gelen haberdir;
/// <see cref="Related"/> aynı olayı veren diğer yayıncılar. Kaç farklı yayıncının haberi verdiği
/// (<see cref="Coverage"/>) önemin en güçlü sinyalidir.
/// </summary>
public sealed class Story(NewsItem lead)
{
    public NewsItem Lead { get; private set; } = lead;
    public List<NewsItem> Related { get; } = [];

    public Topic? Topic { get; set; }
    public int Relevance { get; set; }
    public double Score { get; set; }
    public string? TurkishSummary { get; set; }

    public IEnumerable<NewsItem> All => Related.Prepend(Lead);

    public int Coverage => All.Select(n => n.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count();

    /// <summary>En yeni yayın zamanı: olay hâlâ gündemde mi?</summary>
    public DateTimeOffset Latest => All.Max(n => n.Published);

    /// <summary>Başka bir kümeyi bu kümeye katar (Gemini'nin bulduğu anlamsal kopyalar için).</summary>
    public void Absorb(Story other)
    {
        Related.AddRange(other.All);
        TurkishSummary ??= other.TurkishSummary;
    }

    /// <summary>Daha iyi bir haber lider olur; eski lider "related" listesine geçer.</summary>
    public void PromoteLead(NewsItem item)
    {
        if (ReferenceEquals(item, Lead)) return;
        Related.Remove(item);
        Related.Insert(0, Lead);
        Lead = item;
    }

    public override string ToString() => $"{Score,5:0.0} [{Topic?.Id ?? "-"}] x{Coverage} {Lead.Title} ({Lead.Source})";
}

public sealed record DigestSection(Topic Topic, List<Story> Stories);

/// <summary>IELTS için günün haberlerinden seçilmiş kelime.</summary>
public sealed record VocabularyItem(
    string Word,
    string PartOfSpeech,
    string MeaningEn,
    string MeaningTr,
    string Example,
    string? StoryUrl);

public sealed record DigestStats(int Fetched, int Stories, int Sources, int Feeds, int FeedsFailed);

/// <summary>Günün bülteni: mail, JSON arşiv ve web sitesi bu modelden üretilir.</summary>
public sealed record Digest(
    DateOnly Date,
    DateTimeOffset GeneratedAt,
    Story? Headline,
    List<DigestSection> Sections,
    List<VocabularyItem> Vocabulary,
    DigestStats Stats,
    string? Model)
{
    public IEnumerable<Story> AllStories => Sections.SelectMany(s => s.Stories).Prepend(Headline).OfType<Story>();
    public int StoryCount => AllStories.Count();
}
