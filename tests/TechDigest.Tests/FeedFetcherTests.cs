using TechDigest.Services;

namespace TechDigest.Tests;

public class FeedFetcherTests
{
    [Fact]
    public void Parse_ReadsItems_CleansHtml_SkipsEmptyTitles()
    {
        using var xml = File.OpenRead(Path.Combine("Fixtures", "sample-rss.xml"));

        var items = FeedFetcher.Parse(xml, "Sample");

        var item = Assert.Single(items);
        Assert.Equal("Nvidia & TSMC sign new deal", item.Title);
        Assert.Equal("Jensen Huang said the deal is big.", item.Summary);
        Assert.Equal("https://example.com/nvidia", item.Link);
        Assert.Equal("Sample", item.Source);
        Assert.Equal(new DateTimeOffset(2026, 11, 21, 8, 0, 0, TimeSpan.Zero), item.Published);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("<b>Hello</b>&nbsp;world", "Hello world")]
    public void Clean_StripsTagsAndEntities(string? input, string expected)
        => Assert.Equal(expected, FeedFetcher.Clean(input));
}
