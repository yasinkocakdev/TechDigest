using System.Net;
using System.Text.Json.Nodes;
using TechDigest.Logging;
using TechDigest.Models;
using TechDigest.Services.Enrichment;
using static TechDigest.Tests.TestKit;

namespace TechDigest.Tests;

public class GeminiClientTests
{
    private static GeminiClient.ModelInfo M(string name, params string[] methods) =>
        new($"models/{name}", methods.Length == 0 ? ["generateContent"] : [.. methods]);

    private static GeminiClient Client(FakeHttp http, string? pinned = null) =>
        new(http.Client(), "test-key", pinned, LogSetup.Null<GeminiClient>())
        {
            RetryDelays = [TimeSpan.Zero],
            PerModelTimeout = TimeSpan.FromSeconds(5),
            TotalBudget = TimeSpan.FromSeconds(10),
        };

    private const string ModelsJson = """{"models":[{"name":"models/gemini-9.0-flash","supportedGenerationMethods":["generateContent"]},{"name":"models/gemini-8.0-flash","supportedGenerationMethods":["generateContent"]}]}""";

    private static HttpResponseMessage Ok(string text) => FakeHttp.Text(
        new JsonObject { ["candidates"] = new JsonArray(new JsonObject { ["content"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = text }) } }) }.ToJsonString(),
        type: "application/json");

    [Fact]
    public void RankModels_TriesThreeNewestStable_ThenLite_ThenOlder_ThenPreview()
    {
        var ranked = GeminiClient.RankModels(
        [
            M("gemini-2.5-flash"), M("gemini-3.5-flash"), M("gemini-3.6-flash"), M("gemini-3.7-flash"), M("gemini-3.8-flash"),
            M("gemini-3.6-flash-lite"), M("gemini-4.0-flash-preview"), M("gemini-3.8-flash-image"), M("gemini-3.0-pro"),
            M("text-embedding-004", "embedContent"),
        ]);
        Assert.Equal(
        [
            "gemini-3.8-flash", "gemini-3.7-flash", "gemini-3.6-flash", "gemini-3.6-flash-lite",
            "gemini-3.5-flash", "gemini-2.5-flash", "gemini-4.0-flash-preview", "gemini-3.8-flash-image",
        ], ranked);
    }

    [Fact]
    public async Task RetriesOverload_ThenFallsBackToNextModel()
    {
        var http = new FakeHttp(r =>
        {
            var path = r.RequestUri!.AbsolutePath;
            if (path.EndsWith("/models")) return FakeHttp.Text(ModelsJson, type: "application/json");
            if (path.Contains("gemini-9.0-flash")) return FakeHttp.Text("""{"error":{"message":"busy"}}""", HttpStatusCode.ServiceUnavailable);
            return Ok("""{"ok":true}""");
        });

        var result = await Client(http).GenerateJsonAsync("prompt", null);

        Assert.Equal("gemini-8.0-flash", result?.Model);
        Assert.Equal(2, http.Requests.Count(r => r.RequestUri!.AbsolutePath.Contains("gemini-9.0-flash"))); // 1 + 1 tekrar
        Assert.All(http.Requests, r => Assert.Equal("test-key", r.Headers.GetValues("x-goog-api-key").Single()));
        Assert.All(http.Requests, r => Assert.DoesNotContain("test-key", r.RequestUri!.ToString()));
    }

    [Fact]
    public async Task PinnedModelNotFound_DiscoversAnother()
    {
        var http = new FakeHttp(r =>
        {
            var path = r.RequestUri!.AbsolutePath;
            if (path.EndsWith("/models")) return FakeHttp.Text(ModelsJson, type: "application/json");
            if (path.Contains("retired-model")) return FakeHttp.Text("{}", HttpStatusCode.NotFound);
            return Ok("{}");
        });

        Assert.Equal("gemini-9.0-flash", (await Client(http, "retired-model").GenerateJsonAsync("p", null))?.Model);
    }

    [Fact]
    public async Task SchemaRejected_RetriesWithoutSchema()
    {
        var calls = 0;
        var http = new FakeHttp(async r =>
        {
            var body = await r.Content!.ReadAsStringAsync();
            calls++;
            return body.Contains("responseSchema") ? FakeHttp.Text("{}", HttpStatusCode.BadRequest) : Ok("{}");
        });

        var result = await Client(http, "m").GenerateJsonAsync("p", new JsonObject { ["type"] = "OBJECT" });

        Assert.NotNull(result);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task InvalidKey_StopsImmediately()
    {
        var http = new FakeHttp(_ => FakeHttp.Text("{}", HttpStatusCode.Forbidden));
        Assert.Null(await Client(http, "m").GenerateJsonAsync("p", null));
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task NeverThrows_OnNetworkFailure()
    {
        Func<HttpRequestMessage, HttpResponseMessage> fail = _ => throw new HttpRequestException("dns");
        var http = new FakeHttp(fail);
        Assert.Null(await Client(http, "m").GenerateJsonAsync("p", null));
    }
}

public class DigestEnricherTests
{
    private static List<Story> Candidates() =>
    [
        new(News("Nvidia halts shipments to Southeast Asia", "Reuters", "Nvidia paused deliveries amid stringent new export rules.")),
        new(News("Nvidia pauses deliveries in Asia", "CNBC")),
        new(News("OpenAI unveils a new model", "The Verge")),
    ];

    [Fact]
    public void BuildPrompt_NumbersEveryCandidate_AndIncludesDescriptions()
    {
        var prompt = DigestEnricher.BuildPrompt(Candidates());
        Assert.Contains("[0] Nvidia halts shipments to Southeast Asia (Reuters)", prompt);
        Assert.Contains("stringent new export rules", prompt);
        Assert.Contains("[2] OpenAI unveils a new model (The Verge)", prompt);
    }

    [Fact]
    public void Parse_ValidatesIds_SanitizesText_AndDedupesVocabulary()
    {
        var json = """
            {
              "summaries": [{"id":0,"tr":" Nvidia\u0007 sevkiyatı durdurdu. "},{"id":9,"tr":"geçersiz"},{"id":2,"tr":""}],
              "duplicateGroups": [[0,1],[2],[1,99]],
              "vocabulary": [
                {"word":"stringent","partOfSpeech":"Adjective","meaningEn":"very strict","meaningTr":"sıkı","example":"amid stringent new export rules","id":0},
                {"word":"Stringent","partOfSpeech":"adj","meaningEn":"x","meaningTr":"y","example":"z","id":0},
                {"word":"unveil","partOfSpeech":"verb","meaningEn":"reveal","meaningTr":"tanıtmak","example":"OpenAI unveils a new model","id":42}
              ]
            }
            """;
        var candidates = Candidates();

        var e = DigestEnricher.Parse(json, candidates, "m")!;

        Assert.Equal("Nvidia sevkiyatı durdurdu.", Assert.Single(e.Summaries).Value);
        Assert.Equal([0, 1], Assert.Single(e.DuplicateGroups));
        Assert.Equal(["stringent", "unveil"], e.Vocabulary.Select(v => v.Word));
        Assert.Equal("adjective", e.Vocabulary[0].PartOfSpeech);
        Assert.Equal(candidates[0].Lead.Link, e.Vocabulary[0].StoryUrl);
        Assert.Null(e.Vocabulary[1].StoryUrl);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"summaries":[],"duplicateGroups":[],"vocabulary":[]}""")]
    public void Parse_ReturnsNull_WhenNothingUsable(string json) => Assert.Null(DigestEnricher.Parse(json, Candidates(), "m"));

    [Fact]
    public void Merger_AppliesSummaries_AndMergesDuplicates_KeepingHigherScore()
    {
        var candidates = Candidates();
        candidates[0].Score = 10;
        candidates[1].Score = 5;
        var enrichment = new Enrichment(new() { [1] = "özet", [2] = "diğer" }, [[0, 1]], [], "m");

        var result = EnrichmentMerger.Apply(enrichment, candidates);

        Assert.Equal(2, result.Count);
        Assert.Same(candidates[0], result[0]);
        Assert.Equal(2, result[0].Coverage);
        Assert.Equal("özet", result[0].TurkishSummary); // kopyanın özeti kaybolmaz
    }
}
