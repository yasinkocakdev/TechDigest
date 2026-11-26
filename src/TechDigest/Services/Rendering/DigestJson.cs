using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using TechDigest.Models;

namespace TechDigest.Services.Rendering;

/// <summary>
/// Bülteni web sitesinin okuduğu JSON'a çevirir ve arşivi günceller.
/// Arşiv düzeni: <c>days/YYYY-MM-DD.json</c> (günün tamamı) + <c>index.json</c> (gün listesi, yeniden eskiye).
/// Site bu dosyaları <c>data/</c> altında okur.
/// </summary>
public static class DigestJson
{
    public const int SchemaVersion = 1;

    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Türkçe karakterler ş yerine ş olarak yazılsın; JSON'u insanlar da okuyabilsin.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static DayDto ToDto(Digest digest) => new(
        Version: SchemaVersion,
        Date: digest.Date.ToString("yyyy-MM-dd"),
        GeneratedAt: digest.GeneratedAt,
        Model: digest.Model,
        Stats: digest.Stats,
        Headline: digest.Headline is null ? null : Story(digest.Headline),
        Sections: digest.Sections.Select(s => new SectionDto(s.Topic.Id, s.Topic.Name, s.Topic.Color, s.Stories.Select(Story).ToList())).ToList(),
        Vocabulary: digest.Vocabulary);

    private static StoryDto Story(Story s) => new(
        Title: s.Lead.Title,
        Summary: s.All.Select(n => n.Summary).FirstOrDefault(x => x.Length > 0),
        Tr: s.TurkishSummary,
        Url: s.Lead.Link,
        Source: s.Lead.Source,
        Published: s.Lead.Published,
        Topic: s.Topic!.Id,
        Coverage: s.Coverage,
        Score: Math.Round(s.Score, 1),
        Related: s.Related
            .DistinctBy(n => n.Source, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .Select(n => new RelatedDto(n.Source, n.Title, n.Link))
            .ToList());

    public static string Serialize(Digest digest) => JsonSerializer.Serialize(ToDto(digest), Options);

    /// <summary>Günü arşive yazar ve index.json'u günceller (aynı gün tekrar çalışırsa üzerine yazar).</summary>
    public static void WriteArchive(string archiveDir, Digest digest)
    {
        var day = ToDto(digest);
        var daysDir = Path.Combine(archiveDir, "days");
        Directory.CreateDirectory(daysDir);
        File.WriteAllText(Path.Combine(daysDir, $"{day.Date}.json"), JsonSerializer.Serialize(day, Options));

        var indexPath = Path.Combine(archiveDir, "index.json");
        var entries = File.Exists(indexPath)
            ? JsonSerializer.Deserialize<IndexDto>(File.ReadAllText(indexPath), Options)?.Days ?? []
            : [];

        var entry = new IndexEntryDto(
            day.Date,
            digest.StoryCount,
            day.Headline?.Title,
            day.Headline?.Topic,
            day.Sections.Select(s => s.Id).ToList());

        entries = entries.Where(e => e.Date != day.Date).Append(entry).OrderByDescending(e => e.Date, StringComparer.Ordinal).ToList();
        File.WriteAllText(indexPath, JsonSerializer.Serialize(new IndexDto(SchemaVersion, DateTimeOffset.UtcNow, entries), Options));
    }

    public sealed record DayDto(int Version, string Date, DateTimeOffset GeneratedAt, string? Model, DigestStats Stats,
        StoryDto? Headline, List<SectionDto> Sections, List<VocabularyItem> Vocabulary);
    public sealed record SectionDto(string Id, string Name, string Color, List<StoryDto> Stories);
    public sealed record StoryDto(string Title, string? Summary, string? Tr, string Url, string Source, DateTimeOffset Published,
        string Topic, int Coverage, double Score, List<RelatedDto> Related);
    public sealed record RelatedDto(string Source, string Title, string Url);
    public sealed record IndexDto(int Version, DateTimeOffset Updated, List<IndexEntryDto> Days);
    public sealed record IndexEntryDto(string Date, int Stories, string? Headline, string? Topic, List<string> Topics);
}
