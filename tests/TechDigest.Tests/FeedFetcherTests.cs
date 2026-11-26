using System.Net;
using System.Text;
using TechDigest.Logging;
using TechDigest.Models;
using TechDigest.Services.Fetching;
using static TechDigest.Tests.TestKit;

namespace TechDigest.Tests;

public class FeedFetcherTests
{
    private static Stream Stream(string s) => new MemoryStream(Encoding.UTF8.GetBytes(s));

    [Fact]
    public void Parse_UsesConfiguredPublisher_CleansText_SkipsUnsafeLinks()
    {
        var xml = Rss(
            ("Nvidia &amp; TSMC sign deal", "https://example.com/a", "<p>Jensen Huang said   it is big.</p>", Now),
            ("Evil", "javascript:alert(1)", "", Now),
            ("", "https://example.com/empty", "", Now));

        var items = FeedFetcher.Parse(Stream(xml), new FeedSource("BBC Technology", "https://x", "BBC"));

        var item = Assert.Single(items);
        Assert.Equal("Nvidia & TSMC sign deal", item.Title);
        Assert.Equal("Jensen Huang said it is big.", item.Summary);
        Assert.Equal("BBC", item.Source);
        Assert.Equal(Now, item.Published);
    }

    [Fact]
    public void Parse_Aggregator_TakesPublisherFromTitle_AndDropsEchoedSummary()
    {
        var xml = Rss(("Nvidia hits record - Bloomberg.com", "https://news.google.com/x", "Nvidia hits record Bloomberg", Now));

        var item = Assert.Single(FeedFetcher.Parse(Stream(xml), new FeedSource("Google News: Chips", "https://x")));

        Assert.Equal("Nvidia hits record", item.Title);
        Assert.Equal("Bloomberg", item.Source);
        Assert.Equal("", item.Summary);
    }

    [Fact]
    public void Parse_CapsItemsPerFeed()
    {
        var many = Enumerable.Range(0, 300).Select(i => ($"Story {i}", $"https://example.com/{i}", "", Now)).ToArray();
        Assert.Equal(FeedFetcher.MaxItemsPerFeed, FeedFetcher.Parse(Stream(Rss(many)), new FeedSource("S", "https://x", "S")).Count);
    }

    [Fact]
    public async Task FetchAll_SkipsBrokenFeeds_AndFiltersByTime()
    {
        var http = new FakeHttp(r => r.RequestUri!.Host switch
        {
            "ok.test" => FakeHttp.Text(Rss(
                ("Fresh", "https://example.com/fresh", "", Now.AddHours(-1)),
                ("Old", "https://example.com/old", "", Now.AddDays(-3)))),
            "down.test" => FakeHttp.Text("", HttpStatusCode.InternalServerError),
            _ => FakeHttp.Text("<not-xml"),
        });
        var fetcher = new FeedFetcher(http.Client(), LogSetup.Null<FeedFetcher>());

        var result = await fetcher.FetchAllAsync(
        [
            new FeedSource("Ok", "https://ok.test/rss", "Ok"),
            new FeedSource("Down", "https://down.test/rss", "Down"),
            new FeedSource("Broken", "https://broken.test/rss", "Broken"),
        ], Now.AddHours(-24));

        Assert.Equal("Fresh", Assert.Single(result.Items).Title);
        Assert.Equal(1, result.FeedsOk);
        Assert.Equal(2, result.FeedsFailed);
    }

    [Fact]
    public async Task FetchAll_RejectsOversizedFeed()
    {
        var huge = new string('x', (int)FeedFetcher.MaxFeedBytes + 10);
        var http = new FakeHttp(_ => FakeHttp.Text($"<rss>{huge}</rss>"));
        var fetcher = new FeedFetcher(http.Client(), LogSetup.Null<FeedFetcher>());

        var result = await fetcher.FetchAllAsync([new FeedSource("Big", "https://big.test/rss", "Big")], Now.AddDays(-1));

        Assert.Empty(result.Items);
        Assert.Equal(1, result.FeedsFailed);
    }
}
