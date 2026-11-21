using TechDigest.Models;
using TechDigest.Services;

namespace TechDigest.Tests;

public class DeduplicatorTests
{
    private static NewsItem News(string title, string link) =>
        new(title, "", link, "Test", DateTimeOffset.UtcNow);

    [Fact]
    public void RemoveDuplicates_DropsSimilarTitles_KeepsFirst()
    {
        var items = new[]
        {
            News("Nvidia shares hit record high after earnings", "https://a"),
            News("Nvidia shares hit a record high after earnings - Reuters", "https://b"),
            News("Anthropic launches new Claude model", "https://c"),
        };

        var result = Deduplicator.RemoveDuplicates(items);

        Assert.Equal(["https://a", "https://c"], result.Select(r => r.Link));
    }

    [Fact]
    public void RemoveDuplicates_DropsSameLink()
    {
        var items = new[] { News("One title", "https://same"), News("Totally different words", "https://same") };
        Assert.Single(Deduplicator.RemoveDuplicates(items));
    }

    [Fact]
    public void Words_IgnoresStopWordsAndPublisherSuffix()
    {
        Assert.Equal(new HashSet<string> { "nvidia", "beat", "estimate" },
            Deduplicator.Words("Nvidia beats the estimates - CNBC"));
    }

    [Fact]
    public void IsSameStory_CatchesRewordedHeadline_ButNotRelatedStory()
    {
        var a = Deduplicator.Words("Treasury secretary rules out further restrictions on Nvidia chip sales to China");
        var b = Deduplicator.Words("Treasury secretary says no further restrictions are needed for Nvidia chip exports to China");
        var c = Deduplicator.Words("Anthropic shares tumble after record-setting IPO");
        var d = Deduplicator.Words("Anthropic CEO Dario Amodei unwinds in southern Italy days after record IPO");

        Assert.True(Deduplicator.IsSameStory(a, b));
        Assert.False(Deduplicator.IsSameStory(c, d));
    }
}
