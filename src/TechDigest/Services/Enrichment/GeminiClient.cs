using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace TechDigest.Services.Enrichment;

public sealed record GeminiResult(string Text, string Model);

/// <summary>
/// Gemini'den JSON cevap alır. Ücretsiz katmanın gerçeklerine göre tasarlandı:
/// <list type="bullet">
/// <item>Modeller emekliye ayrılıyor (404) → anahtarın erişebildiği modeller listelenip en uygunu seçilir.</item>
/// <item>Yeni modeller sık sık yoğun (503/429) → bekleyip tekrar dener, sonra sıradaki modele geçer.</item>
/// <item>Bazen dakikalarca cevap gelmez → model başına zaman aşımı, tüm iş için de toplam süre sınırı.</item>
/// </list>
/// Hiçbir durumda exception fırlatmaz; başarısızlıkta null döner ve bülten Gemini'siz gönderilir.
/// </summary>
public sealed class GeminiClient(HttpClient http, string apiKey, string? pinnedModel, ILogger<GeminiClient> log)
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public int MaxModels { get; init; } = 5;
    public TimeSpan PerModelTimeout { get; init; } = TimeSpan.FromMinutes(3);
    public TimeSpan TotalBudget { get; init; } = TimeSpan.FromMinutes(8);
    public TimeSpan[] RetryDelays { get; init; } = [TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    public async Task<GeminiResult?> GenerateJsonAsync(string prompt, JsonObject? schema, CancellationToken ct = default)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TotalBudget);
        try
        {
            var candidates = pinnedModel is not null ? [pinnedModel] : await ListModelsAsync(budget.Token);
            var listed = pinnedModel is null;

            for (var i = 0; i < candidates.Count && i < MaxModels; i++)
            {
                var model = candidates[i];
                var (status, text) = await GenerateWithRetryAsync(model, prompt, schema, budget.Token);

                // Şema reddedildiyse (eski/yeni API farkı) şemasız bir kez dene: prompt zaten JSON istiyor.
                if (status == HttpStatusCode.BadRequest && schema is not null)
                {
                    log.LogWarning("Gemini şemayı reddetti, şemasız deneniyor.");
                    (status, text) = await GenerateAsync(model, prompt, null, budget.Token);
                }

                if (status == HttpStatusCode.OK && text is not null) return new GeminiResult(text, model);
                if (status is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    return null; // anahtar ya da istek sorunu: başka model denemek fayda etmez

                if (!listed)
                {
                    candidates = [.. candidates, .. (await ListModelsAsync(budget.Token)).Where(m => m != pinnedModel)];
                    listed = true;
                }
                if (i + 1 < Math.Min(candidates.Count, MaxModels))
                    log.LogInformation("→ '{Model}' olmadı, '{Next}' deneniyor.", model, candidates[i + 1]);
            }
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            log.LogWarning("Gemini için ayrılan {Minutes} dakika doldu.", TotalBudget.TotalMinutes);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            log.LogWarning("Gemini: {Message}", ex.Message);
            return null;
        }
    }

    private async Task<(HttpStatusCode, string?)> GenerateWithRetryAsync(string model, string prompt, JsonObject? schema, CancellationToken ct)
    {
        var (status, text) = await GenerateAsync(model, prompt, schema, ct);
        foreach (var delay in RetryDelays)
        {
            if (!IsTransient(status)) break;
            log.LogInformation("… Gemini yoğun ({Status}), {Seconds} sn sonra tekrar.", (int)status, delay.TotalSeconds);
            await Task.Delay(delay, ct);
            (status, text) = await GenerateAsync(model, prompt, schema, ct);
        }
        return (status, text);
    }

    public static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError or HttpStatusCode.GatewayTimeout;

    private async Task<(HttpStatusCode, string?)> GenerateAsync(string model, string prompt, JsonObject? schema, CancellationToken ct)
    {
        var generationConfig = new JsonObject { ["responseMimeType"] = "application/json", ["temperature"] = 0.2 };
        if (schema is not null) generationConfig["responseSchema"] = schema.DeepClone();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/models/{model}:generateContent")
        {
            // Anahtar URL'de değil header'da: loglarda/proxy kayıtlarında görünmesin.
            Headers = { { "x-goog-api-key", apiKey } },
            Content = JsonContent.Create(new JsonObject
            {
                ["contents"] = new JsonArray(new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = prompt }) }),
                ["generationConfig"] = generationConfig,
            }),
        };

        using var perModel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        perModel.CancelAfter(PerModelTimeout);
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, perModel.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            log.LogWarning("'{Model}' {Seconds} sn içinde cevap vermedi.", model, PerModelTimeout.TotalSeconds);
            return (HttpStatusCode.RequestTimeout, null);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                await LogErrorAsync(model, response, ct);
                return (response.StatusCode, null);
            }
            var body = await response.Content.ReadFromJsonAsync<GenerateResponse>(Json, ct);
            var text = body?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
            return (response.StatusCode, text);
        }
    }

    private async Task<List<string>> ListModelsAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/models?pageSize=1000")
        {
            Headers = { { "x-goog-api-key", apiKey } },
        };
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            await LogErrorAsync("models", response, ct);
            return [];
        }
        var list = await response.Content.ReadFromJsonAsync<ModelList>(Json, ct);
        var ranked = RankModels(list?.Models ?? []);
        log.LogInformation("Gemini modelleri: {Models}", ranked.Count == 0 ? "(uygun model yok)" : string.Join(", ", ranked.Take(MaxModels)));
        return ranked;
    }

    private async Task LogErrorAsync(string model, HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        // Gemini hata mesajı genelde "error.message" içinde; sadece onu, kısaltarak yaz.
        var message = TryGetErrorMessage(body) ?? body;
        log.LogWarning("Gemini {Model}: HTTP {Status} {Message}", model, (int)response.StatusCode,
            message.Length > 160 ? message[..160] + "…" : message.ReplaceLineEndings(" "));
    }

    private static string? TryGetErrorMessage(string body)
    {
        try { return JsonNode.Parse(body)?["error"]?["message"]?.GetValue<string>(); }
        catch (JsonException) { return null; }
    }

    private static readonly string[] AvoidWords = ["image", "tts", "live", "thinking", "exp", "audio", "embedding"];

    /// <summary>
    /// Deneme sırası: en yeni 3 kararlı flash → lite modeller (daha az yoğun) → eski kararlı flash'lar
    /// → preview → diğerleri. Her grupta yeni sürüm önce. Ağdan bağımsız, test edilebilir.
    /// </summary>
    public static List<string> RankModels(IEnumerable<ModelInfo> models)
    {
        var flash = models
            .Where(m => m.SupportedGenerationMethods?.Contains("generateContent") == true)
            .Select(m => m.Name.Replace("models/", ""))
            .Where(n => n.Contains("flash", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(VersionOf)
            .ThenBy(n => n.Length) // aynı sürümde "gemini-3.0-flash", "gemini-3.0-flash-001"den önce
            .ToList();

        bool Has(string n, string w) => n.Contains(w, StringComparison.OrdinalIgnoreCase);
        bool Clean(string n) => !AvoidWords.Any(w => Has(n, w)) && !Has(n, "preview");

        var stable = flash.Where(n => Clean(n) && !Has(n, "lite")).ToList();
        var lite = flash.Where(n => Clean(n) && Has(n, "lite")).ToList();
        var preview = flash.Where(n => !AvoidWords.Any(w => Has(n, w)) && Has(n, "preview")).ToList();

        return stable.Take(3).Concat(lite).Concat(stable.Skip(3)).Concat(preview).Concat(flash).Distinct().ToList();
    }

    private static Version VersionOf(string name)
    {
        var m = Regex.Match(name, @"(\d+)(?:\.(\d+))?");
        return m.Success
            ? new Version(int.Parse(m.Groups[1].Value), m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0)
            : new Version(0, 0);
    }

    public sealed record ModelInfo(string Name, List<string>? SupportedGenerationMethods);
    private sealed record ModelList(List<ModelInfo>? Models);
    private sealed record GenerateResponse(List<Candidate>? Candidates);
    private sealed record Candidate(Content? Content);
    private sealed record Content(List<Part>? Parts);
    private sealed record Part(string? Text);
}
