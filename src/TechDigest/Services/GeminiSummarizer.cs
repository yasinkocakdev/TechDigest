using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TechDigest.Models;

namespace TechDigest.Services;

/// <summary>
/// Bütün haberleri TEK istekte Gemini'ye gönderip her biri için 1-2 cümlelik Türkçe özet ister.
/// Tek istek = ücretsiz katmanın günlük limitine takılmamak.
/// Hata olursa exception fırlatmaz; mail Türkçe özetsiz gider.
/// </summary>
public sealed class GeminiSummarizer(HttpClient http, string apiKey, string model)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<bool> AddTurkishSummariesAsync(IReadOnlyList<DigestItem> items, CancellationToken ct = default)
    {
        if (items.Count == 0) return true;
        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                // Anahtar URL'de değil header'da: loglarda görünmesin.
                Headers = { { "x-goog-api-key", apiKey } },
                Content = JsonContent.Create(new
                {
                    contents = new[] { new { parts = new[] { new { text = BuildPrompt(items) } } } },
                    generationConfig = new { responseMimeType = "application/json", temperature = 0.2 }
                })
            };

            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"  ✗ Gemini: HTTP {(int)response.StatusCode}");
                return false;
            }

            var body = await response.Content.ReadFromJsonAsync<GeminiResponse>(Json, ct);
            var text = body?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
            return Apply(items, text);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Console.WriteLine($"  ✗ Gemini: {ex.Message}");
            return false;
        }
    }

    public static string BuildPrompt(IReadOnlyList<DigestItem> items)
    {
        var sb = new StringBuilder("""
            You are a news editor for a Turkish software developer.
            For each news item below, write a 1-2 sentence summary in natural Turkish
            explaining what happened and why it matters. Use only the given information; do not invent facts.
            Keep company and product names as they are.
            Return ONLY a JSON array like: [{"id": 0, "tr": "..."}, ...]

            """);
        for (var i = 0; i < items.Count; i++)
        {
            var n = items[i].News;
            sb.Append($"\n[{i}] {n.Title} ({n.Source})");
            if (n.Summary.Length > 0) sb.Append($"\n    {EmailRenderer.Truncate(n.Summary)}");
        }
        return sb.ToString();
    }

    /// <summary>Gemini'nin JSON cevabını haberlere dağıtır. Ağdan bağımsız, test edilebilir.</summary>
    public static bool Apply(IReadOnlyList<DigestItem> items, string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            var summaries = JsonSerializer.Deserialize<List<Summary>>(json, Json) ?? [];
            foreach (var s in summaries.Where(s => s.Id >= 0 && s.Id < items.Count && !string.IsNullOrWhiteSpace(s.Tr)))
                items[s.Id].TurkishSummary = s.Tr.Trim();
            return summaries.Count > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record Summary(int Id, string Tr);

    private sealed record GeminiResponse(List<Candidate>? Candidates);
    private sealed record Candidate(Content? Content);
    private sealed record Content(List<Part>? Parts);
    private sealed record Part(string? Text);
}
