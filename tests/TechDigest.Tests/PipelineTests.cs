using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using TechDigest.Configuration;
using TechDigest.Logging;
using TechDigest.Models;
using TechDigest.Pipeline;
using TechDigest.Services.Enrichment;
using TechDigest.Services.Fetching;
using static TechDigest.Tests.TestKit;

namespace TechDigest.Tests;

/// <summary>Uçtan uca: sahte RSS + sahte Gemini ile boru hattının tamamı, ağa çıkmadan.</summary>
public class PipelineTests
{
    private static DigestConfig TestConfig() => Config() with
    {
        Sources =
        [
            new FeedSource("Wire", "https://wire.test/rss", "Reuters"),
            new FeedSource("Tech", "https://tech.test/rss", "The Verge"),
            new FeedSource("Down", "https://down.test/rss", "CNBC"),
        ],
    };

    private static HttpResponseMessage Feeds(HttpRequestMessage r) => r.RequestUri!.Host switch
    {
        "wire.test" => FakeHttp.Text(Rss(
            ("Nvidia halts chip shipments to Southeast Asia", "https://wire.test/nvidia", "Nvidia paused deliveries amid stringent export rules.", Now.AddHours(-2)),
            ("Ransomware gang hits European hospital network", "https://wire.test/ransom", "", Now.AddHours(-5)),
            ("OpenAI unveils new reasoning model", "https://wire.test/openai", "", Now.AddHours(-1)),
            ("Old story about Nvidia chips", "https://wire.test/old", "", Now.AddDays(-3)))),
        "tech.test" => FakeHttp.Text(Rss(
            ("Nvidia halts chip shipments to Southeast Asia nations", "https://tech.test/nvidia", "", Now.AddHours(-1)),
            ("Which AI chip stock is the better buy?", "https://tech.test/clickbait", "", Now.AddHours(-1)),
            ("Local bakery wins award", "https://tech.test/bakery", "", Now.AddHours(-1)))),
        _ => FakeHttp.Text("", HttpStatusCode.ServiceUnavailable),
    };

    private static HttpResponseMessage Gemini(HttpRequestMessage r)
    {
        if (r.RequestUri!.AbsolutePath.EndsWith("/models"))
            return FakeHttp.Text("""{"models":[{"name":"models/gemini-9.0-flash","supportedGenerationMethods":["generateContent"]}]}""", type: "application/json");
        var answer = """
            {"summaries":[{"id":0,"tr":"Nvidia sevkiyatı durdurdu."},{"id":1,"tr":"OpenAI yeni model tanıttı."}],
             "duplicateGroups":[],
             "vocabulary":[{"word":"stringent","partOfSpeech":"adjective","meaningEn":"very strict","meaningTr":"sıkı","example":"amid stringent export rules","id":0}]}
            """;
        var body = new JsonObject { ["candidates"] = new JsonArray(new JsonObject { ["content"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = answer }) } }) };
        return FakeHttp.Text(body.ToJsonString(), type: "application/json");
    }

    private static (DigestPipeline Pipeline, AppOptions Options) Build(bool withGemini, string? archive = null)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var options = new AppOptions { DryRun = true, OutputDir = Path.Combine(dir, "out"), ArchiveDir = archive, SiteUrl = "https://site.test/" };
        var fetcher = new FeedFetcher(new FakeHttp(Feeds).Client(), LogSetup.Null<FeedFetcher>());
        var enricher = withGemini
            ? new DigestEnricher(new GeminiClient(new FakeHttp(Gemini).Client(), "k", null, LogSetup.Null<GeminiClient>()), LogSetup.Null<DigestEnricher>())
            : null;
        return (new DigestPipeline(TestConfig(), options, fetcher, enricher, null, LogSetup.Null<DigestPipeline>(), () => Now), options);
    }

    [Fact]
    public async Task FullRun_WithGemini_ProducesCuratedDigestAndFiles()
    {
        var (pipeline, options) = Build(withGemini: true);

        var digest = await pipeline.RunAsync();

        Assert.Equal(new DateOnly(2026, 11, 27), digest.Date);
        Assert.Equal("Nvidia halts chip shipments to Southeast Asia", digest.Headline!.Lead.Title); // 2 yayıncı → manşet
        Assert.Equal(2, digest.Headline.Coverage);
        Assert.Equal("Nvidia sevkiyatı durdurdu.", digest.Headline.TurkishSummary);
        var titles = digest.AllStories.Select(s => s.Lead.Title).ToList();
        Assert.DoesNotContain(titles, t => t.Contains("better buy") || t.Contains("bakery") || t.Contains("Old story"));
        Assert.Contains("Ransomware gang hits European hospital network", titles);
        Assert.Equal("stringent", Assert.Single(digest.Vocabulary).Word);
        Assert.Equal("gemini-9.0-flash", digest.Model);
        Assert.Equal(1, digest.Stats.FeedsFailed);

        Assert.True(File.Exists(Path.Combine(options.OutputDir, "email.html")));
        Assert.True(File.Exists(Path.Combine(options.OutputDir, "email.txt")));
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(options.OutputDir, "digest.json")));
        Assert.Equal("2026-11-27", json.RootElement.GetProperty("date").GetString());
    }

    [Fact]
    public async Task FullRun_WithoutGemini_StillProducesDigest()
    {
        var digest = await Build(withGemini: false).Pipeline.RunAsync();
        Assert.NotNull(digest.Headline);
        Assert.Null(digest.Headline!.TurkishSummary);
        Assert.Empty(digest.Vocabulary);
        Assert.Null(digest.Model);
    }

    [Fact]
    public async Task DryRun_NeverWritesArchive()
    {
        var archive = Directory.CreateTempSubdirectory().FullName;
        await Build(withGemini: false, archive).Pipeline.RunAsync();
        Assert.False(File.Exists(Path.Combine(archive, "index.json")));
    }
}
