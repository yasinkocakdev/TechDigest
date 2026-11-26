using System.Text.Json;
using TechDigest.Configuration;
using TechDigest.Models;
using TechDigest.Services.Delivery;
using TechDigest.Services.Rendering;
using static TechDigest.Tests.TestKit;

namespace TechDigest.Tests;

public class EmailRendererTests
{
    internal static Digest SampleDigest(bool withHeadline = true)
    {
        var topic = Topic("chips");
        var headline = new Story(News("<script>alert(1)</script> Nvidia halts shipments", "Reuters", "Summary & more", hoursAgo: 3))
        {
            Topic = topic, Score = 30, TurkishSummary = "Nvidia sevkiyatı durdurdu.",
        };
        headline.Related.Add(News("Nvidia pauses shipments", "Bloomberg"));
        var other = new Story(News("TSMC expands Arizona plant", "CNBC")) { Topic = topic, Score = 12 };

        return new Digest(
            new DateOnly(2026, 11, 27), Now,
            withHeadline ? headline : null,
            [new DigestSection(topic, [other])],
            [new VocabularyItem("stringent", "adjective", "very strict", "sıkı", "amid <b>stringent</b> rules", "https://example.com/a")],
            new DigestStats(Fetched: 300, Stories: 2, Sources: 3, Feeds: 12, FeedsFailed: 1),
            "gemini-test");
    }

    private static readonly EmailLinks Links = new("https://site.test/#/2026-11-27", "https://site.test/#/archive", "https://github.com/x/y");

    [Fact]
    public void Html_EscapesEverything_FromFeeds()
    {
        var html = EmailRenderer.RenderHtml(SampleDigest(), Now, Links);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("Summary &amp; more", html);
        Assert.DoesNotContain("<b>stringent</b>", html);
    }

    [Fact]
    public void Html_HasHeadline_TurkishBlock_Coverage_Vocabulary_AndLinks()
    {
        var html = EmailRenderer.RenderHtml(SampleDigest(), Now, Links);
        Assert.Contains("Top story", html);
        Assert.Contains(">TR</span>", html);
        Assert.Contains("Nvidia sevkiyatı durdurdu.", html);
        Assert.Contains("2 outlets", html);
        Assert.Contains("3h ago", html);
        Assert.Contains("IELTS vocabulary", html);
        Assert.Contains("https://site.test/#/2026-11-27", html);
        Assert.Contains("prefers-color-scheme: dark", html);
        Assert.Contains("Also reported by Bloomberg", html);
    }

    [Fact]
    public void Html_WorksWithoutHeadlineOrLinks()
    {
        var html = EmailRenderer.RenderHtml(SampleDigest(withHeadline: false), Now, new EmailLinks(null, null, null));
        Assert.DoesNotContain("Top story", html);
        Assert.DoesNotContain("View in browser", html);
    }

    [Fact]
    public void Highlight_MarksWord_WithoutBreakingEscaping()
    {
        var result = EmailRenderer.Highlight("amid <Stringent> rules", "stringent");
        Assert.StartsWith("amid &lt;<span", result);
        Assert.Contains(">Stringent</span>&gt; rules", result);
    }

    [Fact]
    public void Highlight_CoversInflectedWord() =>
        Assert.Contains(">Halts</span> sales", EmailRenderer.Highlight("Nvidia Halts sales", "halt"));

    [Fact]
    public void Subject_And_Preheader_AreInformative()
    {
        var digest = SampleDigest();
        Assert.StartsWith("TechDigest · 27 Nov — ", EmailRenderer.Subject(digest));
        Assert.Equal("2 stories · 1 IELTS words · CHIPS", EmailRenderer.Preheader(digest));
    }

    [Fact]
    public void Text_ContainsAllParts()
    {
        var text = EmailRenderer.RenderText(SampleDigest(), Now, Links);
        Assert.Contains("== TOP STORY ==", text);
        Assert.Contains("TR: Nvidia sevkiyatı durdurdu.", text);
        Assert.Contains("== IELTS VOCABULARY ==", text);
    }

    [Theory]
    [InlineData(0.5, "just now")]
    [InlineData(45, "45m ago")]
    [InlineData(180, "3h ago")]
    [InlineData(60 * 30, "1d ago")]
    public void TimeText_Ago(double minutes, string expected) =>
        Assert.Equal(expected, TimeText.Ago(Now.AddMinutes(-minutes), Now));
}

public class DigestJsonTests
{
    [Fact]
    public void Serialize_ProducesSiteSchema()
    {
        using var doc = JsonDocument.Parse(DigestJson.Serialize(EmailRendererTests.SampleDigest()));
        var root = doc.RootElement;
        Assert.Equal("2026-11-27", root.GetProperty("date").GetString());
        Assert.Equal(2, root.GetProperty("headline").GetProperty("coverage").GetInt32());
        Assert.Equal("chips", root.GetProperty("sections")[0].GetProperty("id").GetString());
        Assert.Equal("sıkı", root.GetProperty("vocabulary")[0].GetProperty("meaningTr").GetString());
    }

    [Fact]
    public void WriteArchive_AddsDay_UpdatesIndex_AndReplacesSameDay()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var d1 = EmailRendererTests.SampleDigest();
        var d0 = d1 with { Date = new DateOnly(2026, 11, 26) };

        DigestJson.WriteArchive(dir, d0);
        DigestJson.WriteArchive(dir, d1);
        DigestJson.WriteArchive(dir, d1); // aynı gün tekrar çalışırsa kopya olmamalı

        Assert.True(File.Exists(Path.Combine(dir, "days", "2026-11-27.json")));
        var index = JsonSerializer.Deserialize<DigestJson.IndexDto>(File.ReadAllText(Path.Combine(dir, "index.json")), DigestJson.Options)!;
        Assert.Equal(["2026-11-27", "2026-11-26"], index.Days.Select(d => d.Date));
        Assert.Equal("chips", index.Days[0].Topic);
    }
}

public class EmailSenderTests
{
    private static readonly EmailMessage Email = new("Subj", "<p>x</p>", "x");

    [Fact]
    public void ParseRecipients_SplitsTrimsAndDedupes() =>
        Assert.Equal(["me@a.com", "friend@b.com"], EmailSender.ParseRecipients(" me@a.com, friend@b.com ;ME@a.com,"));

    [Fact]
    public void BuildMessage_PutsRecipientsInBcc_SenderInTo()
    {
        var message = EmailSender.BuildMessage("sender@gmail.com", "me@a.com, friend@b.com", Email);
        Assert.Equal(["sender@gmail.com"], message.To.Mailboxes.Select(m => m.Address));
        Assert.Equal(["me@a.com", "friend@b.com"], message.Bcc.Mailboxes.Select(m => m.Address));
    }

    [Fact]
    public void BuildMessage_ThrowsWhenNoRecipients() =>
        Assert.Throws<InvalidOperationException>(() => EmailSender.BuildMessage("s@gmail.com", " , ", Email));
}

public class AppOptionsTests
{
    private static Func<string, string?> Env(params (string Key, string Value)[] vars) =>
        name => vars.FirstOrDefault(v => v.Key == name).Value;

    [Fact]
    public void DerivesSiteAndRepoUrl_FromGitHubRepository()
    {
        var o = AppOptions.FromEnvironment([], Env(("GITHUB_REPOSITORY", "YasinKocakDev/TechDigest")));
        Assert.Equal("https://yasinkocakdev.github.io/TechDigest/", o.SiteUrl);
        Assert.Equal("https://github.com/YasinKocakDev/TechDigest", o.RepositoryUrl);
        Assert.Equal("https://yasinkocakdev.github.io/TechDigest/#/2026-11-27", o.DayUrl(new DateOnly(2026, 11, 27)));
    }

    [Fact]
    public void Validate_RequiresMailSettings_UnlessDryRun()
    {
        Assert.Equal(["GMAIL_ADDRESS", "GMAIL_APP_PASSWORD", "TO_ADDRESS"], AppOptions.FromEnvironment([], Env()).Validate());
        Assert.Empty(AppOptions.FromEnvironment(["--dry-run"], Env()).Validate());
    }

    [Fact]
    public void Config_LoadsAndIsConsistent()
    {
        var config = Config();
        Assert.NotEmpty(config.Sources);
        Assert.All(config.Sources, s => Assert.StartsWith("https://", s.Url));
        Assert.Equal(config.Topics.Topics.Count, config.Topics.Topics.Select(t => t.Id).Distinct().Count());
        Assert.All(config.Topics.Topics, t => Assert.Matches("^#[0-9a-f]{6}$", t.Color));
    }
}
