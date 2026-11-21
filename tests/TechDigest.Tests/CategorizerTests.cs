using TechDigest.Models;
using TechDigest.Services;

namespace TechDigest.Tests;

public class CategorizerTests
{
    private static readonly Topic Chips = new("Chips", "🔌", ["nvidia", "chip"]);
    private static readonly Topic Ai = new("AI", "🤖", ["anthropic", "ai model"]);

    private static NewsItem News(string title, string summary = "") =>
        new(title, summary, $"https://x/{title.GetHashCode()}", "Test", DateTimeOffset.UtcNow);

    [Fact]
    public void Score_TitleMatchWeighsMoreThanSummary()
    {
        Assert.Equal(3, Categorizer.Score(News("Nvidia results"), Chips));
        Assert.Equal(1, Categorizer.Score(News("Results", "Nvidia beat estimates"), Chips));
    }

    [Fact]
    public void Score_MatchesWholeWordsOnly()
    {
        // "chip" "chipotle" içinde eşleşmemeli.
        Assert.Equal(0, Categorizer.Score(News("Chipotle opens new store"), Chips));
    }

    [Fact]
    public void Categorize_AssignsBestTopic_DropsUnmatched_RespectsLimits()
    {
        var config = new TopicConfig(MaxPerTopic: 2, MaxTotal: 3, [Chips, Ai]);
        var items = new[]
        {
            News("Nvidia unveils chip"),          // Chips (6)
            News("Nvidia shares rise"),           // Chips (3)
            News("Nvidia chip export ban"),       // Chips (6)
            News("Anthropic releases AI model"),  // AI (6)
            News("Football match tonight"),       // hiçbiri
        };

        var sections = new Categorizer(config).Categorize(items);

        Assert.Equal(3, sections.Sum(s => s.Items.Count));
        var chips = sections.Single(s => s.Topic == Chips);
        Assert.Equal(2, chips.Items.Count);
        Assert.All(chips.Items, d => Assert.Equal(6, d.Score));
        Assert.Single(sections.Single(s => s.Topic == Ai).Items);
    }

    [Fact]
    public void Categorize_DropsExcludedStockTips()
    {
        var config = new TopicConfig(5, 25, [Chips]) { ExcludeKeywords = ["better buy"] };
        var items = new[] { News("AMD vs. Nvidia: Which chip stock is the better buy?"), News("Nvidia unveils chip") };

        var section = Assert.Single(new Categorizer(config).Categorize(items));
        Assert.Equal("Nvidia unveils chip", Assert.Single(section.Items).News.Title);
    }
}
