using TechDigest.Models;
using TechDigest.Services;

namespace TechDigest.Tests;

public class GeminiSummarizerTests
{
    private static List<DigestItem> Items(int n) => Enumerable.Range(0, n)
        .Select(i => new DigestItem(new NewsItem($"Title {i}", "", $"https://x/{i}", "S", DateTimeOffset.UtcNow), 1))
        .ToList();

    [Fact]
    public void Apply_SetsSummariesById_IgnoresOutOfRange()
    {
        var items = Items(2);
        var ok = GeminiSummarizer.Apply(items, """[{"id":1,"tr":" İkinci "},{"id":7,"tr":"yok"}]""");

        Assert.True(ok);
        Assert.Null(items[0].TurkishSummary);
        Assert.Equal("İkinci", items[1].TurkishSummary);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("[]")]
    public void Apply_ReturnsFalseOnBadResponse(string? json) =>
        Assert.False(GeminiSummarizer.Apply(Items(1), json));

    [Fact]
    public void BuildPrompt_NumbersEveryItem()
    {
        var prompt = GeminiSummarizer.BuildPrompt(Items(3));
        Assert.Contains("[0] Title 0", prompt);
        Assert.Contains("[2] Title 2", prompt);
    }

    private static GeminiSummarizer.ModelInfo M(string name, params string[] methods) =>
        new($"models/{name}", methods.Length == 0 ? ["generateContent"] : [.. methods]);

    [Fact]
    public void PickModel_PrefersNewestStableFlash()
    {
        var models = new[]
        {
            M("gemini-2.0-flash"), M("gemini-3.0-flash"), M("gemini-3.0-flash-lite"),
            M("gemini-3.0-pro"), M("gemini-3.5-flash-preview"), M("gemini-3.0-flash-001"),
            M("text-embedding-004", "embedContent"),
        };
        Assert.Equal("gemini-3.0-flash", GeminiSummarizer.PickModel(models));
    }

    [Fact]
    public void PickModel_FallsBackToPreview_ThenNull()
    {
        Assert.Equal("gemini-4.0-flash-preview",
            GeminiSummarizer.PickModel([M("gemini-4.0-flash-preview"), M("gemini-3.0-pro")]));
        Assert.Null(GeminiSummarizer.PickModel([M("gemini-3.0-pro"), M("gemini-3.0-flash", "embedContent")]));
    }

    [Fact]
    public void RankModels_OrdersStableThenPreviewThenOthers()
    {
        var ranked = GeminiSummarizer.RankModels(
            [M("gemini-3.0-flash-lite"), M("gemini-3.0-flash"), M("gemini-4.0-flash-preview"), M("gemini-3.8-flash")]);
        Assert.Equal(["gemini-3.8-flash", "gemini-3.0-flash", "gemini-4.0-flash-preview", "gemini-3.0-flash-lite"], ranked);
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(System.Net.HttpStatusCode.TooManyRequests, true)]
    [InlineData(System.Net.HttpStatusCode.NotFound, false)]
    [InlineData(System.Net.HttpStatusCode.Forbidden, false)]
    public void IsTransient_OnlyForOverloadErrors(System.Net.HttpStatusCode status, bool expected) =>
        Assert.Equal(expected, GeminiSummarizer.IsTransient(status));
}
