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
}
