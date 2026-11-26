using TechDigest.Models;
using TechDigest.Services.Ranking;
using static TechDigest.Tests.TestKit;

namespace TechDigest.Tests;

public class PublisherDirectoryTests
{
    [Theory]
    [InlineData("Reuters", PublisherTier.Top)]
    [InlineData("Bloomberg.com", PublisherTier.Top)]
    [InlineData("the verge", PublisherTier.Trusted)]
    [InlineData("Benzinga", PublisherTier.Low)]
    [InlineData("The Motley Fool", PublisherTier.Blocked)]
    [InlineData("Some Random Blog", PublisherTier.Normal)]
    public void TierOf(string publisher, PublisherTier expected) => Assert.Equal(expected, Publishers().TierOf(publisher));
}

public class ContentFilterTests
{
    [Theory]
    [InlineData("Nvidia unveils new chip", "Reuters", true)]
    [InlineData("AMD vs. Nvidia: Which chip stock is the better buy?", "Reuters", false)]
    [InlineData("Nvidia unveils new chip", "Seeking Alpha", false)]
    public void Allows(string title, string source, bool expected)
    {
        var config = Config();
        Assert.Equal(expected, new ContentFilter(config.Topics, new PublisherDirectory(config.Publishers)).Allows(News(title, source)));
    }
}

public class TopicMatcherTests
{
    private readonly TopicMatcher _matcher = new(Config().Topics.Topics);

    [Fact]
    public void StrongKeywordInTitle_AssignsTopic() =>
        Assert.Equal("chips", _matcher.Match(News("Nvidia halts shipments to Southeast Asia")).Topic?.Id);

    [Fact]
    public void WeakKeywordAlone_DoesNotAssignTopic() =>
        Assert.Null(_matcher.Match(News("China celebrates national holiday")).Topic);

    [Fact]
    public void MatchesWholeWordsOnly() =>
        Assert.Null(_matcher.Match(News("Chipotle opens new restaurant")).Topic);

    [Fact]
    public void SummaryMatchCountsLessThanTitle()
    {
        var inTitle = _matcher.Match(News("Ransomware hits hospital"));
        var inSummary = _matcher.Match(News("Hospital systems down", summary: "A ransomware gang claimed the attack."));
        Assert.Equal("security", inSummary.Topic?.Id);
        Assert.True(inTitle.Relevance > inSummary.Relevance);
    }
}

public class StoryClustererTests
{
    private readonly StoryClusterer _clusterer = new(Publishers());

    [Fact]
    public void GroupsSameEvent_CountsPublishers_AndPicksBestLead()
    {
        var stories = _clusterer.Cluster(
        [
            News("Nvidia shares hit record high after earnings beat", "Benzinga"),
            News("Nvidia shares hit a record high after earnings", "Reuters", summary: "Shares rose 5%."),
            News("Nvidia shares hit record high after strong earnings", "CNBC"),
            News("Anthropic launches new Claude model", "The Verge"),
        ]);

        Assert.Equal(2, stories.Count);
        var nvidia = stories.Single(s => s.Lead.Title.Contains("Nvidia"));
        Assert.Equal("Reuters", nvidia.Lead.Source);
        Assert.Equal(3, nvidia.Coverage);
    }

    [Fact]
    public void SamePublisherTwice_CountsOnce()
    {
        var story = Assert.Single(_clusterer.Cluster(
        [
            News("Nvidia shares hit record high after earnings", "Reuters", link: "https://reuters.com/1"),
            News("Nvidia shares hit record high after earnings beat", "Reuters", link: "https://reuters.com/2"),
        ]));
        Assert.Equal(1, story.Coverage);
    }

    [Fact]
    public void RareWordPass_MergesRewordedCoverage_ButKeepsDifferentEventsApart()
    {
        // Gerçekçi bir gün: birçok farklı haber (IDF için), aynı olayın farklı yazımları ve aynı şirketin
        // farklı bir haberi.
        var filler = Enumerable.Range(0, 40).Select(i => News($"Unrelated market story number {i} about stocks and shares {i}", $"Outlet{i}"));
        var stories = _clusterer.Cluster(filler.Concat(
        [
            News("FIFA says hackers accessed personal data in cyberattack", "Bloomberg"),
            News("FIFA confirms cyberattack, hackers stole personal and business data", "Mashable"),
            News("Nvidia halts chip shipments to Southeast Asia", "Reuters"),
            News("Nvidia CEO Jensen Huang returns to Denny's", "CNN"),
        ]));

        Assert.Equal(2, stories.Single(s => s.Lead.Title.StartsWith("FIFA")).Coverage);
        Assert.Equal(2, stories.Count(s => s.Lead.Title.StartsWith("Nvidia")));
    }
}

public class StoryRankerTests
{
    private static StoryRanker Ranker(int maxPerTopic = 4, int maxTotal = 20)
    {
        var config = Config();
        return new StoryRanker(config.Topics with { MaxPerTopic = maxPerTopic, MaxTotal = maxTotal }, new PublisherDirectory(config.Publishers));
    }

    private static Story Story(NewsItem lead, params NewsItem[] related)
    {
        var s = new Story(lead);
        s.Related.AddRange(related);
        return s;
    }

    [Fact]
    public void Coverage_BeatsKeywordStuffing()
    {
        var stuffed = Story(News("Nvidia AMD Intel TSMC chip GPU semiconductor stocks", "Some Blog"));
        var covered = Story(News("Nvidia halts shipments", "Reuters"),
            News("Nvidia pauses shipments", "Bloomberg"), News("Nvidia stops shipments", "CNBC"), News("Nvidia shipments halted", "The Verge"));

        var ranked = Ranker().Rank([stuffed, covered], Now);

        Assert.Same(covered, ranked[0]);
    }

    [Fact]
    public void LowTierPublisher_IsPenalised()
    {
        var ranker = Ranker();
        var low = Story(News("Nvidia halts chip shipments", "Benzinga"));
        var top = Story(News("Nvidia halts chip shipments", "Reuters"));
        ranker.Rank([low, top], Now);
        Assert.True(top.Score > low.Score);
    }

    [Fact]
    public void Rank_DropsStoriesWithoutTopic() =>
        Assert.Empty(Ranker().Rank([Story(News("Local bakery wins award", "BBC"))], Now));

    [Fact]
    public void Compose_PicksHeadline_RespectsLimits_KeepsTopicOrder()
    {
        var ranker = Ranker(maxPerTopic: 2, maxTotal: 4);
        var stories = new[]
        {
            Story(News("Nvidia halts shipments", "Reuters"), News("Nvidia pauses shipments", "Bloomberg")),
            Story(News("Nvidia chip one", "Reuters")), Story(News("Nvidia chip two", "Reuters")), Story(News("Nvidia chip three", "Reuters")),
            Story(News("OpenAI releases new model", "Reuters")),
            Story(News("Ransomware hits hospital", "Reuters")),
        };

        var (headline, sections) = ranker.Compose(ranker.Rank(stories, Now));

        Assert.Equal("Nvidia halts shipments", headline!.Lead.Title);
        Assert.DoesNotContain(sections.SelectMany(s => s.Stories), s => ReferenceEquals(s, headline));
        Assert.Equal(3, sections.Sum(s => s.Stories.Count)); // toplam 4 = manşet + 3
        Assert.All(sections, s => Assert.True(s.Stories.Count <= 2));
        var order = Config().Topics.Topics.Select(t => t.Id).ToList();
        Assert.Equal(sections.Select(s => s.Topic.Id).OrderBy(order.IndexOf), sections.Select(s => s.Topic.Id));
    }
}
