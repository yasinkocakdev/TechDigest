using System.Net;
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
public sealed class GeminiSummarizer(HttpClient http, string apiKey, string? model = null)
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Son kullanılan model; log'a yazmak için.</summary>
    public string? Model { get; private set; } = model;

    public async Task<bool> AddTurkishSummariesAsync(IReadOnlyList<DigestItem> items, CancellationToken ct = default)
    {
        if (items.Count == 0) return true;
        try
        {
            // Model verilmemişse en güncel flash modelini kendimiz buluruz.
            Model ??= await FindModelAsync(ct);
            if (Model is null) return false;

            var (status, text) = await GenerateAsync(Model, BuildPrompt(items), ct);

            // Model kaldırılmış olabilir (Google eski modelleri emekliye ayırıyor): yenisini bul, bir kez dene.
            if (status == HttpStatusCode.NotFound)
            {
                Console.WriteLine($"  ✗ Gemini: '{Model}' bulunamadı, güncel model aranıyor...");
                var fallback = await FindModelAsync(ct);
                if (fallback is null || fallback == Model) return false;
                Model = fallback;
                (status, text) = await GenerateAsync(Model, BuildPrompt(items), ct);
            }

            return status == HttpStatusCode.OK && Apply(items, text);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Console.WriteLine($"  ✗ Gemini: {ex.Message}");
            return false;
        }
    }

    private async Task<(HttpStatusCode Status, string? Text)> GenerateAsync(string modelName, string prompt, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/models/{modelName}:generateContent")
        {
            // Anahtar URL'de değil header'da: loglarda görünmesin.
            Headers = { { "x-goog-api-key", apiKey } },
            Content = JsonContent.Create(new
            {
                contents = new[] { new { parts = new[] { new { text = prompt } } } },
                generationConfig = new { responseMimeType = "application/json", temperature = 0.2 }
            })
        };

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            await LogErrorAsync(response, ct);
            return (response.StatusCode, null);
        }

        var body = await response.Content.ReadFromJsonAsync<GeminiResponse>(Json, ct);
        return (response.StatusCode, body?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text);
    }

    /// <summary>Anahtarın erişebildiği modelleri listeler ve en uygununu seçer.</summary>
    private async Task<string?> FindModelAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/models?pageSize=1000")
        {
            Headers = { { "x-goog-api-key", apiKey } }
        };
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            await LogErrorAsync(response, ct);
            return null;
        }

        var list = await response.Content.ReadFromJsonAsync<ModelList>(Json, ct);
        var picked = PickModel(list?.Models ?? []);
        Console.WriteLine(picked is null ? "  ✗ Gemini: uygun model bulunamadı." : $"  ✓ Gemini modeli seçildi: {picked}");
        return picked;
    }

    private static async Task LogErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        var snippet = body.Length > 200 ? body[..200] + "…" : body;
        Console.WriteLine($"  ✗ Gemini: HTTP {(int)response.StatusCode} {snippet.ReplaceLineEndings(" ")}");
    }

    private static readonly string[] AvoidWords = ["lite", "image", "tts", "live", "thinking", "exp", "audio", "embedding"];

    /// <summary>
    /// generateContent destekleyen "flash" modellerinden en yüksek sürümlüsünü seçer.
    /// Önce kararlı sürümler, yoksa preview, o da yoksa herhangi bir flash modeli.
    /// Ağdan bağımsız, test edilebilir.
    /// </summary>
    public static string? PickModel(IEnumerable<ModelInfo> models)
    {
        var flash = models
            .Where(m => m.SupportedGenerationMethods?.Contains("generateContent") == true)
            .Select(m => m.Name.Replace("models/", ""))
            .Where(n => n.Contains("flash", StringComparison.OrdinalIgnoreCase))
            .ToList();

        bool Clean(string n) => !AvoidWords.Any(w => n.Contains(w, StringComparison.OrdinalIgnoreCase));
        bool Preview(string n) => n.Contains("preview", StringComparison.OrdinalIgnoreCase);

        var candidates = flash.Where(n => Clean(n) && !Preview(n)).ToList();
        if (candidates.Count == 0) candidates = flash.Where(Clean).ToList();
        if (candidates.Count == 0) candidates = flash;

        return candidates
            .OrderByDescending(VersionOf)
            .ThenBy(n => n.Length) // aynı sürümde "gemini-3.0-flash", "gemini-3.0-flash-001"den önce gelsin
            .FirstOrDefault();
    }

    private static Version VersionOf(string name)
    {
        var match = System.Text.RegularExpressions.Regex.Match(name, @"(\d+)(?:\.(\d+))?");
        return match.Success
            ? new Version(int.Parse(match.Groups[1].Value), match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0)
            : new Version(0, 0);
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

    public sealed record ModelInfo(string Name, List<string>? SupportedGenerationMethods);
    private sealed record ModelList(List<ModelInfo>? Models);

    private sealed record GeminiResponse(List<Candidate>? Candidates);
    private sealed record Candidate(Content? Content);
    private sealed record Content(List<Part>? Parts);
    private sealed record Part(string? Text);
}
