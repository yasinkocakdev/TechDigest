using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TechDigest.Models;
using TechDigest.Text;

namespace TechDigest.Services.Enrichment;

/// <summary>Gemini'nin tek bir istekte ürettiği her şey.</summary>
public sealed record Enrichment(
    Dictionary<int, string> Summaries,
    List<List<int>> DuplicateGroups,
    List<VocabularyItem> Vocabulary,
    string Model);

/// <summary>
/// Aday haberleri TEK istekte Gemini'ye gönderir ve üç şey ister:
/// her habere 1–2 cümlelik Türkçe özet, aynı olayı anlatan haber grupları (kural tabanlı kümelemenin
/// kaçırdığı, farklı kelimelerle yazılmış kopyalar) ve IELTS için 5 kelime.
/// Tek istek = ücretsiz katman limitine takılmamak.
/// </summary>
public sealed class DigestEnricher(GeminiClient gemini, ILogger<DigestEnricher> log)
{
    public const int MaxSummaryLength = 320;

    public async Task<Enrichment?> EnrichAsync(IReadOnlyList<Story> candidates, CancellationToken ct = default)
    {
        if (candidates.Count == 0) return null;
        var result = await gemini.GenerateJsonAsync(BuildPrompt(candidates), Schema, ct);
        if (result is null) return null;

        var enrichment = Parse(result.Text, candidates, result.Model);
        if (enrichment is null) log.LogWarning("Gemini cevabı okunamadı ({Model}).", result.Model);
        return enrichment;
    }

    public static string BuildPrompt(IReadOnlyList<Story> candidates)
    {
        var sb = new StringBuilder("""
            You are the editor of a daily tech news digest for a Turkish software developer who reads the
            news in English to prepare for the IELTS exam.

            Tasks, using ONLY the information given below (never invent facts):
            1. "summaries": for EVERY item, 1–2 sentences in natural Turkish: what happened and why it matters.
               Keep company/product names as they are. Max 300 characters each.
            2. "duplicateGroups": groups of item ids that report the SAME event (e.g. the same announcement
               worded differently). Only clear duplicates; omit items that are merely related. Each group ≥ 2 ids.
            3. "vocabulary": exactly 5 useful B2–C1 English words or short phrases that appear in the titles or
               descriptions below (not company names, not basic words). For each: the word as it appears,
               part of speech, a short English definition, the Turkish meaning, the sentence/phrase from the
               text where it appears (verbatim), and the id of that item.

            Items:
            """);
        for (var i = 0; i < candidates.Count; i++)
        {
            var lead = candidates[i].Lead;
            sb.Append($"\n[{i}] {lead.Title} ({lead.Source})");
            var description = candidates[i].All.Select(n => n.Summary).FirstOrDefault(s => s.Length > 0);
            if (description is not null) sb.Append($"\n    {TextTools.Truncate(description, 300)}");
        }
        return sb.ToString();
    }

    /// <summary>Gemini'nin yapılandırılmış çıktı şeması (OpenAPI alt kümesi).</summary>
    public static readonly JsonObject Schema = (JsonObject)JsonNode.Parse("""
        {
          "type": "OBJECT",
          "properties": {
            "summaries": {
              "type": "ARRAY",
              "items": { "type": "OBJECT", "properties": { "id": { "type": "INTEGER" }, "tr": { "type": "STRING" } }, "required": ["id", "tr"] }
            },
            "duplicateGroups": { "type": "ARRAY", "items": { "type": "ARRAY", "items": { "type": "INTEGER" } } },
            "vocabulary": {
              "type": "ARRAY",
              "items": {
                "type": "OBJECT",
                "properties": {
                  "word": { "type": "STRING" }, "partOfSpeech": { "type": "STRING" }, "meaningEn": { "type": "STRING" },
                  "meaningTr": { "type": "STRING" }, "example": { "type": "STRING" }, "id": { "type": "INTEGER" }
                },
                "required": ["word", "partOfSpeech", "meaningEn", "meaningTr", "example", "id"]
              }
            }
          },
          "required": ["summaries", "duplicateGroups", "vocabulary"]
        }
        """)!;

    /// <summary>
    /// Cevabı doğrular ve temizler: geçersiz id'ler atılır, metinler kısaltılır ve kontrol karakterlerinden
    /// arındırılır. Model ne döndürürse döndürsün mail/site bozulmaz. Ağdan bağımsız, test edilebilir.
    /// </summary>
    public static Enrichment? Parse(string json, IReadOnlyList<Story> candidates, string model)
    {
        Response? response;
        try
        {
            response = JsonSerializer.Deserialize<Response>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return null;
        }
        if (response is null) return null;

        bool Valid(int id) => id >= 0 && id < candidates.Count;

        var summaries = (response.Summaries ?? [])
            .Where(s => Valid(s.Id) && !string.IsNullOrWhiteSpace(s.Tr))
            .GroupBy(s => s.Id)
            .ToDictionary(g => g.Key, g => TextTools.Sanitize(g.First().Tr, MaxSummaryLength));

        var groups = (response.DuplicateGroups ?? [])
            .Select(g => g.Where(Valid).Distinct().ToList())
            .Where(g => g.Count >= 2)
            .ToList();

        var vocabulary = (response.Vocabulary ?? [])
            .Where(v => !string.IsNullOrWhiteSpace(v.Word) && !string.IsNullOrWhiteSpace(v.MeaningTr))
            .DistinctBy(v => v.Word!.Trim().ToLowerInvariant())
            .Take(5)
            .Select(v => new VocabularyItem(
                Word: TextTools.Sanitize(v.Word, 40),
                PartOfSpeech: TextTools.Sanitize(v.PartOfSpeech, 20).ToLowerInvariant(),
                MeaningEn: TextTools.Sanitize(v.MeaningEn, 160),
                MeaningTr: TextTools.Sanitize(v.MeaningTr, 120),
                Example: TextTools.Sanitize(v.Example, 240),
                StoryUrl: Valid(v.Id) ? candidates[v.Id].Lead.Link : null))
            .ToList();

        if (summaries.Count == 0 && vocabulary.Count == 0) return null;
        return new Enrichment(summaries, groups, vocabulary, model);
    }

    private sealed record Response(List<SummaryDto>? Summaries, List<List<int>>? DuplicateGroups, List<VocabularyDto>? Vocabulary);
    private sealed record SummaryDto(int Id, string? Tr);
    private sealed record VocabularyDto(string? Word, string? PartOfSpeech, string? MeaningEn, string? MeaningTr, string? Example, int Id);
}
