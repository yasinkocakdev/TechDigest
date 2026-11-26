using System.Globalization;
using Microsoft.Extensions.Logging;
using TechDigest.Configuration;
using TechDigest.Logging;
using TechDigest.Pipeline;
using TechDigest.Services.Delivery;
using TechDigest.Services.Enrichment;
using TechDigest.Services.Fetching;

// TechDigest: her sabah teknoloji haberlerini toplar, seçer, özetler ve mail atar.
//
//   dotnet run --project src/TechDigest                → gönderir (GMAIL_ADDRESS, GMAIL_APP_PASSWORD, TO_ADDRESS)
//   dotnet run --project src/TechDigest -- --dry-run   → göndermez; out/email.html, out/digest.json yazar
//   ... --verbose                                      → puanlama ayrıntılarını da gösterir
//
// Opsiyonel: GEMINI_API_KEY (Türkçe özet + IELTS), GEMINI_MODEL, ARCHIVE_DIR, SITE_URL, OUTPUT_DIR

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var options = AppOptions.FromEnvironment(args);
using var logs = LogSetup.Create(options.Verbose);
var log = logs.CreateLogger("TechDigest");

var missing = options.Validate();
if (missing.Count > 0)
{
    log.LogError("Eksik ayar: {Missing}. Göndermeden denemek için --dry-run kullan.", string.Join(", ", missing));
    return 2;
}

try
{
    var config = ConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "config"));

    using var feedHttp = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = System.Net.DecompressionMethods.All })
    {
        Timeout = TimeSpan.FromSeconds(30),
    };
    // Bazı siteler User-Agent olmayan istekleri reddediyor.
    feedHttp.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; TechDigest/2.0; +https://github.com/yasinkocakdev/TechDigest)");

    // Gemini'nin kendi zaman aşımları var (model başına + toplam); HttpClient'ınki onları kesmesin.
    using var geminiHttp = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    var enricher = options.GeminiApiKey is null
        ? null
        : new DigestEnricher(
            new GeminiClient(geminiHttp, options.GeminiApiKey, options.GeminiModel, logs.CreateLogger<GeminiClient>()),
            logs.CreateLogger<DigestEnricher>());

    var sender = options.DryRun
        ? null
        : new EmailSender(options.GmailAddress!, options.GmailAppPassword!, logs.CreateLogger<EmailSender>());

    var pipeline = new DigestPipeline(config, options, new FeedFetcher(feedHttp, logs.CreateLogger<FeedFetcher>()),
        enricher, sender, logs.CreateLogger<DigestPipeline>());

    await pipeline.RunAsync();
    return 0;
}
catch (Exception ex)
{
    log.LogCritical(ex, "Bülten üretilemedi.");
    return 1;
}
