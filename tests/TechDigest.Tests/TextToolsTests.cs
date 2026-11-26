using TechDigest.Text;

namespace TechDigest.Tests;

public class TextToolsTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("<p>Jensen Huang said   the deal</p> is big &amp; bold.", "Jensen Huang said the deal is big & bold.")]
    [InlineData("<b>Hello</b>&nbsp;world", "Hello world")]
    public void Clean_StripsTagsEntitiesAndWhitespace(string? input, string expected) =>
        Assert.Equal(expected, TextTools.Clean(input));

    [Fact]
    public void Sanitize_RemovesControlCharacters_AndCapsLength()
    {
        Assert.Equal("a b c", TextTools.Sanitize("a\u0000b\u0007\nc", 100));
        Assert.True(TextTools.Sanitize(new string('x', 500), 50).Length <= 51);
    }

    [Fact]
    public void Truncate_CutsAtWordBoundary()
    {
        var result = TextTools.Truncate("Nvidia halts chip shipments to Southeast Asia amid curbs", 30);
        Assert.Equal("Nvidia halts chip shipments…", result);
    }

    [Theory]
    [InlineData("https://reuters.com/a", "https://reuters.com/a")]
    [InlineData("http://example.com/b?x=1", "http://example.com/b?x=1")]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("data:text/html,<script>", null)]
    [InlineData("/relative/path", null)]
    [InlineData("", null)]
    public void SafeUrl_AllowsOnlyHttp(string input, string? expected) => Assert.Equal(expected, TextTools.SafeUrl(input));

    [Fact]
    public void Words_DropsStopWords_AndStemsPlurals() =>
        Assert.Equal(new HashSet<string> { "nvidia", "halt", "export", "china" },
            TextTools.Words("Nvidia halts the exports to China"));

    [Fact]
    public void IsSameStory_CatchesRewordedHeadline_ButNotRelatedStory()
    {
        var a = TextTools.Words("Treasury secretary rules out further restrictions on Nvidia chip sales to China");
        var b = TextTools.Words("Treasury secretary says no further restrictions are needed for Nvidia chip exports to China");
        var c = TextTools.Words("Anthropic shares tumble after record-setting IPO");
        var d = TextTools.Words("Anthropic CEO Dario Amodei unwinds in southern Italy days after record IPO");

        Assert.True(TextTools.IsSameStory(a, b));
        Assert.False(TextTools.IsSameStory(c, d));
    }

    [Theory]
    [InlineData("The Wall Street Journal", "wall street journal")]
    [InlineData("Bloomberg.com", "bloomberg")]
    [InlineData("  CNBC ", "cnbc")]
    [InlineData("24/7 Wall St.", "24/7 wall st")]
    public void NormalizePublisher(string input, string expected) => Assert.Equal(expected, TextTools.NormalizePublisher(input));

    [Theory]
    [InlineData("Bloomberg.com", "Bloomberg")]
    [InlineData("Reuters", "Reuters")]
    [InlineData("https://www.internationalfinance.com/", "internationalfinance")]
    public void DisplayPublisher_DropsDotCom(string input, string expected) => Assert.Equal(expected, TextTools.DisplayPublisher(input));
}
