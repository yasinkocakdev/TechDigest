using TechDigest.Models;
using TechDigest.Services;

namespace TechDigest.Tests;

public class EmailRendererTests
{
    [Fact]
    public void RenderHtml_EscapesHtml_IncludesTurkishSummary()
    {
        var news = new NewsItem("<script>alert(1)</script> Nvidia", "Summary & more", "https://x/1", "Reuters", DateTimeOffset.UtcNow);
        var item = new DigestItem(news, 3) { TurkishSummary = "Nvidia rekor kırdı." };
        var sections = new List<DigestSection> { new(new Topic("Chips", "🔌", []), [item]) };

        var html = EmailRenderer.RenderHtml(sections, DateTimeOffset.UtcNow);

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("Summary &amp; more", html);
        Assert.Contains("🇹🇷 Nvidia rekor kırdı.", html);
        Assert.Contains("https://x/1", html);
    }

    [Fact]
    public void Truncate_ShortensLongText()
    {
        var text = new string('a', 500);
        var result = EmailRenderer.Truncate(text);
        Assert.True(result.Length <= 281);
        Assert.EndsWith("…", result);
    }
}
