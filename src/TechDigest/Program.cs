using System.Globalization;
using System.Text.Json;
using TechDigest.Models;
using TechDigest.Services;

// Akış: RSS çek → kopyaları ele → konulara ayır → Türkçe özet (Gemini) → HTML → Gmail.
// Kullanım:
//   dotnet run                → mail gönderir (GMAIL_ADDRESS, GMAIL_APP_PASSWORD, TO_ADDRESS gerekli)
//   dotnet run -- --dry-run   → mail göndermez, digest.html dosyası yazar

var dryRun = args.Contains("--dry-run");
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = new CultureInfo("en-US");
var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var configDir = Path.Combine(AppContext.BaseDirectory, "config");

var sources = JsonSerializer.Deserialize<List<FeedSource>>(
    File.ReadAllText(Path.Combine(configDir, "sources.json")), jsonOptions)!;
var topicConfig = JsonSerializer.Deserialize<TopicConfig>(
    File.ReadAllText(Path.Combine(configDir, "topics.json")), jsonOptions)!;

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
// Bazı siteler User-Agent olmayan istekleri reddediyor.
http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; TechDigest/1.0)");

// 1. Haberleri çek
Console.WriteLine($"1) {sources.Count} kaynak çekiliyor...");
var items = await new FeedFetcher(http).FetchAllAsync(sources, TimeSpan.FromHours(24));
Console.WriteLine($"   Son 24 saatte {items.Count} haber.");

// 2. Kopyaları ele + konulara ayır
var unique = Deduplicator.RemoveDuplicates(items);
var sections = new Categorizer(topicConfig).Categorize(unique);
var digestItems = sections.SelectMany(s => s.Items).ToList();
Console.WriteLine($"2) Kopyalar elendi: {unique.Count}. Mailde: {digestItems.Count} haber, {sections.Count} konu.");

// 3. Türkçe özet (anahtar yoksa ya da hata olursa atlanır)
var geminiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
if (string.IsNullOrWhiteSpace(geminiKey))
{
    Console.WriteLine("3) GEMINI_API_KEY yok, Türkçe özet atlandı.");
}
else
{
    // GEMINI_MODEL verilmezse en güncel flash modeli otomatik seçilir.
    var model = Environment.GetEnvironmentVariable("GEMINI_MODEL") is { Length: > 0 } m ? m : null;
    var summarizer = new GeminiSummarizer(http, geminiKey, model);
    var ok = await summarizer.AddTurkishSummariesAsync(digestItems);
    Console.WriteLine(ok ? $"3) Türkçe özetler eklendi ({summarizer.Model})." : "3) Türkçe özet alınamadı, mail İngilizce gidecek.");
}

// 4. Mail oluştur
var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3)); // İstanbul (UTC+3)
var html = EmailRenderer.RenderHtml(sections, now);
var text = EmailRenderer.RenderText(sections, now);
var subject = EmailRenderer.Subject(now, digestItems.Count);

if (dryRun)
{
    File.WriteAllText("digest.html", html);
    Console.WriteLine($"4) --dry-run: mail gönderilmedi, {Path.GetFullPath("digest.html")} yazıldı.");
    return 0;
}

// 5. Gönder
string Require(string name) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } v
        ? v
        : throw new InvalidOperationException($"{name} ortam değişkeni eksik.");

await new EmailSender(Require("GMAIL_ADDRESS"), Require("GMAIL_APP_PASSWORD"))
    .SendAsync(Require("TO_ADDRESS"), subject, html, text);
Console.WriteLine($"4) Mail gönderildi: \"{subject}\"");
return 0;
