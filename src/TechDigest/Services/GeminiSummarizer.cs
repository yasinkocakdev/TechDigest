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
    public string? Model { get; private set; }

    /// <summary>Yoğunluk (503/429/500) durumunda her modelde bekleme süreleri.</summary>
    public static TimeSpan[] RetryDelays { get; set; } = [TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];
    private const int MaxModels = 3;

    public async Task<bool> AddTurkishSummariesAsync(IReadOnlyList<DigestItem> items, CancellationToken ct = default)
    {
        if (items.Count == 0) return true;
        try
        {
            var prompt = BuildPrompt(items);
            // GEMINI_MODEL verildiyse önce o denenir; olmazsa ya da verilmediyse güncel flash modelleri sırayla.
            var candidates = model is not null ? [model] : await FindModelsAsync(ct);
            var listedModels = model is null;

            for (var i = 0; i < candidates.Count && i < MaxModels; i++)
            {
                Model = candidates[i];
                var (status, text) = await GenerateWithRetryAsync(Model, prompt, ct);

                if (status == HttpStatusCode.OK) return Apply(items, text);
                if (status is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    return false; // anahtar/istek sorunu: başka model denemek fayda etmez

                // 404 (model kaldırılmış) ya da sürekli yoğunluk: sıradaki modele geç.
                if (!listedModels)
                {
                    candidates = [.. candidates, .. (await FindModelsAsync(ct)).Where(m => m != model)];
                    listedModels = true;
                }
                if (i + 1 < candidates.Count && i + 1 < MaxModels)
                    Console.WriteLine($"  → Gemini: '{Model}' olmadı, '{candidates[i + 1]}' deneniyor...");
            }
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Console.WriteLine($"  ✗ Gemini: {ex.Message}");
            return false;
        }
    }

    private async Task<(HttpStatusCode Status, string? Text)> GenerateWithRetryAsync(string modelName, string prompt, CancellationToken ct)
    {
        var (status, text) = await GenerateAsync(modelName, prompt, ct);
        foreach (var delay in RetryDelays)
        {
            if (!IsTransient(status)) break;
            Console.WriteLine($"  … Gemini yoğun, {delay.TotalSeconds:0} sn sonra tekrar denenecek.");
            await Task.Delay(delay, ct);
            (status, text) = await GenerateAsync(modelName, prompt, ct);
        }
        return (status, text);
    }

    public static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError or HttpStatusCode.GatewayTimeout;

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

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            // HttpClient zaman aşımı: yavaş model. Aynı modeli tekrar denemek yerine sıradakine geçilir.
            Console.WriteLine($"  ✗ Gemini: '{modelName}' {http.Timeout.TotalSeconds:0} sn içinde cevap vermedi.");
            return (HttpStatusCode.RequestTimeout, null);
        }

        using var _ = response;
        if (!response.IsSuccessStatusCode)
        {
            await LogErrorAsync(response, ct);
            return (response.StatusCode, null);
        }

        var body = await response.Content.ReadFromJsonAsync<GeminiResponse>(Json, ct);
        return (response.StatusCode, body?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text);
    }

    /// <summary>Anahtarın erişebildiği modelleri listeler, uygunluk sırasına koyar.</summary>
    private async Task<List<string>> FindModelsAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/models?pageSize=1000")
        {
            Headers = { { "x-goog-api-key", apiKey } }
        };
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            await LogErrorAsync(response, ct);
            return [];
        }

        var list = await response.Content.ReadFromJsonAsync<ModelList>(Json, ct);
        var ranked = RankModels(list?.Models ?? []);
        Console.WriteLine(ranked.Count == 0
            ? "  ✗ Gemini: uygun model bulunamadı."
            : $"  ✓ Gemini modelleri: {string.Join(", ", ranked.Take(MaxModels))}");
        return ranked;
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
    public static string? PickModel(IEnumerable<ModelInfo> models) => RankModels(models).FirstOrDefault();

    /// <summary>Uygun flash modellerini tercih sırasına koyar: kararlı → preview → diğer; her grupta yeni sürüm önce.</summary>
    public static List<string> RankModels(IEnumerable<ModelInfo> models)
    {
        var flash = models
            .Where(m => m.SupportedGenerationMethods?.Contains("generateContent") == true)
            .Select(m => m.Name.Replace("models/", ""))
            .Where(n => n.Contains("flash", StringComparison.OrdinalIgnoreCase))
            .ToList();

        bool Clean(string n) => !AvoidWords.Any(w => n.Contains(w, StringComparison.OrdinalIgnoreCase));
        bool Preview(string n) => n.Contains("preview", StringComparison.OrdinalIgnoreCase);

        int Tier(string n) => Clean(n) ? (Preview(n) ? 1 : 0) : 2;

        return flash
            .OrderBy(Tier)
            .ThenByDescending(VersionOf)
            .ThenBy(n => n.Length) // aynı sürümde "gemini-3.0-flash", "gemini-3.0-flash-001"den önce gelsin
            .ToList();
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
