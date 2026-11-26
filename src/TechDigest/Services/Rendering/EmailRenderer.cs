using System.Globalization;
using System.Net;
using System.Text;
using TechDigest.Models;
using TechDigest.Text;
using static TechDigest.Services.Rendering.Theme;

namespace TechDigest.Services.Rendering;

/// <summary>Mailde gösterilecek linkler. Site yoksa (yerel çalışma) null olabilir.</summary>
public sealed record EmailLinks(string? Browser, string? Archive, string? Repository);

/// <summary>
/// Bültenden mail gövdesi üretir.
///
/// Mail HTML'i web HTML'i değildir: Gmail &lt;style&gt; içindeki çoğu şeyi yok sayar, Outlook flexbox
/// bilmez. Bu yüzden düzen tablolarla kurulur ve her stil satır içi yazılır. &lt;style&gt; bloğu sadece
/// destekleyen istemciler için "ek": karanlık mod (Apple Mail/iCloud), mobil boşluklar ve hafif bir
/// giriş animasyonu. Bunları silen istemcide mail yine düzgün görünür.
/// </summary>
public static class EmailRenderer
{
    private const int CardSummaryLength = 240;
    private const int HeadlineSummaryLength = 360;

    public static string Subject(Digest digest)
    {
        var headline = digest.Headline is { } h ? TextTools.Truncate(h.Lead.Title, 70) : "No major stories today";
        return $"TechDigest · {digest.Date:dd MMM} — {headline}";
    }

    /// <summary>Gelen kutusunda konu satırının altında görünen ön izleme metni.</summary>
    public static string Preheader(Digest digest)
    {
        var topics = string.Join(", ", digest.Sections.Select(s => s.Topic.Name.Split(' ')[0]));
        var vocab = digest.Vocabulary.Count > 0 ? $" · {digest.Vocabulary.Count} IELTS words" : "";
        return $"{digest.StoryCount} stories{vocab} · {topics}";
    }

    public static string RenderHtml(Digest digest, DateTimeOffset now, EmailLinks links)
    {
        var sb = new StringBuilder(32_000);
        Head(sb, digest);

        sb.Append($"""<body class="bg" style="margin:0;padding:0;background:{Page};-webkit-text-size-adjust:100%;">""");
        // Ön izleme metni + istemcinin gövdeden metin çekmesini engelleyen boşluk dolgusu.
        sb.Append($"""<div style="display:none;max-height:0;overflow:hidden;opacity:0;">{E(Preheader(digest))}{string.Concat(Enumerable.Repeat("&#8199;&#65279;&#847; ", 40))}</div>""");
        sb.Append($"""<table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" class="bg" style="background:{Page};"><tr><td align="center" class="px" style="padding:28px 16px 40px;">""");
        sb.Append("""<table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="max-width:640px;">""");

        Masthead(sb, digest, links);
        TopicNav(sb, digest);
        if (digest.Headline is { } headline) Headline(sb, headline, now);
        else Row(sb, $"""<p class="tx" style="margin:24px 0;font:16px/1.6 {Font};color:{Body};">No matching stories in the last 24 hours.</p>""");

        foreach (var section in digest.Sections) Section(sb, section, now);
        if (digest.Vocabulary.Count > 0) Vocabulary(sb, digest.Vocabulary);
        Footer(sb, digest, links);

        sb.Append("</table></td></tr></table></body></html>");
        return sb.ToString();
    }

    private static void Head(StringBuilder sb, Digest digest)
    {
        // $$""" : tek süslü parantez CSS'e aittir, {{…}} C# ifadesidir.
        sb.Append($$"""
            <!doctype html>
            <html lang="en" xmlns="http://www.w3.org/1999/xhtml">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <meta name="color-scheme" content="light dark">
            <meta name="supported-color-schemes" content="light dark">
            <title>{{E(Subject(digest))}}</title>
            <style>
              a { text-decoration:none; }
              @media (max-width:600px) {
                .px { padding-left:12px !important; padding-right:12px !important; }
                .pad { padding:18px !important; }
                .h-xl { font-size:23px !important; }
              }
              @media (prefers-color-scheme: dark) {
                .bg { background:{{DarkPage}} !important; }
                .card { background:{{DarkCard}} !important; border-color:{{DarkLine}} !important; }
                .tx { color:{{DarkBody}} !important; }
                .ink, .ink a { color:{{DarkInk}} !important; }
                .mut { color:{{DarkMuted}} !important; }
                .line { border-color:{{DarkLine}} !important; }
                .soft { background:{{DarkSoft}} !important; color:{{DarkBody}} !important; }
                .chip { background:{{DarkSoft}} !important; color:{{DarkBody}} !important; border-color:{{DarkLine}} !important; }
                .hl { background:#3b2f0b !important; color:#fde68a !important; }
              }
              @keyframes rise { from { opacity:0; transform:translateY(10px); } to { opacity:1; transform:none; } }
              @media (prefers-reduced-motion: no-preference) {
                .rise { animation: rise .6s cubic-bezier(.2,.7,.2,1) both; }
                .d1 { animation-delay:.08s; } .d2 { animation-delay:.16s; }
              }
            </style>
            </head>
            """);
    }

    private static void Masthead(StringBuilder sb, Digest digest, EmailLinks links)
    {
        var date = digest.Date.ToString("dddd, d MMMM yyyy", CultureInfo.InvariantCulture);
        if (links.Browser is not null)
            Row(sb, $"""<div style="padding:0 4px 14px;text-align:right;"><a href="{E(links.Browser)}" class="mut" style="color:{Muted};font:500 12px {Font};">View in browser →</a></div>""");

        Row(sb, $"""
            <div class="rise" style="padding:0 4px 18px;">
              <div class="ink" style="font:800 28px/1 {Font};letter-spacing:-0.6px;color:{Ink};">Tech<span style="color:{Brand};">Digest</span></div>
              <div class="mut" style="margin-top:10px;font:600 13px/1.5 {Font};color:{Muted};">{E(date)}</div>
              <div class="mut" style="font:500 13px/1.5 {Font};color:{Muted};">{digest.StoryCount} stories · {digest.Stats.Sources} outlets · {digest.Sections.Count} topics</div>
            </div>
            """);
    }

    private static void TopicNav(StringBuilder sb, Digest digest)
    {
        if (digest.Sections.Count == 0) return;
        var chips = string.Concat(digest.Sections.Select(s =>
            $"""<a href="#{s.Topic.Id}" class="chip" style="display:inline-block;margin:0 6px 8px 0;padding:5px 11px;border:1px solid {Line};border-radius:999px;background:{Card};color:{Body};font:600 12px/1.2 {Font};"><span style="color:{s.Topic.Color};">●</span>&nbsp;{E(s.Topic.Name)}&nbsp;<span class="mut" style="color:{Muted};font-weight:500;">{s.Stories.Count}</span></a>"""));
        Row(sb, $"""<div class="rise d1" style="padding:0 0 14px;">{chips}</div>""");
    }

    private static void Headline(StringBuilder sb, Story story, DateTimeOffset now)
    {
        var topic = story.Topic!;
        var lead = story.Lead;
        var also = story.Related.Select(n => n.Source).Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList();
        var alsoText = also.Count == 0 ? "" :
            $"""<div class="mut" style="margin-top:12px;font:500 12px/1.5 {Font};color:{Muted};">Also reported by {E(string.Join(", ", also))}{(story.Coverage - 1 > also.Count ? " and more" : "")}</div>""";

        Row(sb, $"""
            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" class="card rise d2" style="background:{Card};border:1px solid {Line};border-top:4px solid {topic.Color};border-radius:14px;">
            <tr><td class="pad" style="padding:26px 26px 24px;">
              <div style="font:800 11px/1 {Font};letter-spacing:1.4px;text-transform:uppercase;color:{topic.Color};">Top story · {E(topic.Name)}</div>
              <div class="ink h-xl" style="margin:12px 0 10px;font:800 26px/1.25 {Font};letter-spacing:-0.4px;color:{Ink};"><a href="{E(lead.Link)}" style="color:{Ink};">{E(lead.Title)}</a></div>
              {Meta(story, now)}
              {Paragraph(story.All.Select(n => n.Summary).FirstOrDefault(s => s.Length > 0), HeadlineSummaryLength, 16)}
              {TurkishBlock(story.TurkishSummary, topic.Color)}
              {alsoText}
            </td></tr></table>
            """);
        Spacer(sb, 30);
    }

    private static void Section(StringBuilder sb, DigestSection section, DateTimeOffset now)
    {
        var topic = section.Topic;
        Row(sb, $"""
            <a name="{E(topic.Id)}" id="{E(topic.Id)}"></a>
            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0"><tr>
              <td style="padding:0 4px 12px;">
                <span style="display:inline-block;width:10px;height:10px;border-radius:3px;background:{topic.Color};"></span>
                <span class="ink" style="font:800 17px/1 {Font};letter-spacing:-0.2px;color:{Ink};">&nbsp;{E(topic.Name)}</span>
                <span class="mut" style="font:600 13px {Font};color:{Muted};">&nbsp;· {section.Stories.Count}</span>
              </td>
            </tr></table>
            """);

        foreach (var story in section.Stories)
        {
            var lead = story.Lead;
            Row(sb, $"""
                <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" class="card" style="background:{Card};border:1px solid {Line};border-left:3px solid {topic.Color};border-radius:12px;">
                <tr><td class="pad" style="padding:18px 20px;">
                  <div class="ink" style="margin:0 0 8px;font:700 17px/1.35 {Font};letter-spacing:-0.2px;color:{Ink};"><a href="{E(lead.Link)}" style="color:{Ink};">{E(lead.Title)}</a></div>
                  {Meta(story, now)}
                  {Paragraph(story.All.Select(n => n.Summary).FirstOrDefault(s => s.Length > 0), CardSummaryLength, 14)}
                  {TurkishBlock(story.TurkishSummary, topic.Color)}
                </td></tr></table>
                """);
            Spacer(sb, 10);
        }
        Spacer(sb, 20);
    }

    private static string Meta(Story story, DateTimeOffset now)
    {
        var coverage = story.Coverage > 1
            ? $"""&nbsp;·&nbsp;<span style="color:{story.Topic!.Color};font-weight:700;">{story.Coverage} outlets</span>"""
            : "";
        return $"""<div class="mut" style="margin:0 0 10px;font:600 12px/1.4 {Font};color:{Muted};">{E(story.Lead.Source)}&nbsp;·&nbsp;{TimeText.Ago(story.Lead.Published, now)}{coverage}</div>""";
    }

    private static string Paragraph(string? text, int max, int size) => string.IsNullOrWhiteSpace(text)
        ? ""
        : $"""<p class="tx" style="margin:0 0 12px;font:400 {size}px/1.6 {Font};color:{Body};">{E(TextTools.Truncate(text, max))}</p>""";

    /// <summary>Türkçe özet: bayrak emojisi yerine "TR" etiketi (Windows bayrak emojisini göstermiyor).</summary>
    private static string TurkishBlock(string? tr, string accent) => string.IsNullOrWhiteSpace(tr)
        ? ""
        : $"""
          <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" class="soft" style="background:{Soft};border-radius:10px;"><tr>
            <td valign="top" style="padding:11px 0 11px 12px;width:30px;"><span style="display:inline-block;padding:3px 6px;border-radius:6px;background:{Turkish};color:#fff;font:800 10px/1 {Font};letter-spacing:.5px;">TR</span></td>
            <td class="tx" style="padding:10px 14px 10px 10px;font:400 14px/1.55 {Font};color:{Body};">{E(tr)}</td>
          </tr></table>
          """;

    private static void Vocabulary(StringBuilder sb, List<VocabularyItem> words)
    {
        var rows = string.Concat(words.Select((w, i) => $"""
            <tr><td class="line" style="padding:16px 0;{(i > 0 ? $"border-top:1px solid {Line};" : "")}">
              <div><span class="ink" style="font:800 18px/1.3 {Font};color:{Ink};">{E(w.Word)}</span>&nbsp;<span class="mut" style="font:italic 500 13px {Font};color:{Muted};">{E(w.PartOfSpeech)}</span></div>
              <div class="tx" style="margin:4px 0 0;font:400 14px/1.5 {Font};color:{Body};">{E(w.MeaningEn)}</div>
              <div style="margin:4px 0 0;font:600 14px/1.5 {Font};color:{Turkish};">TR: {E(w.MeaningTr)}</div>
              <div class="mut" style="margin:8px 0 0;font:italic 400 13px/1.55 {Font};color:{Muted};">“{Highlight(w.Example, w.Word)}”</div>
            </td></tr>
            """));

        Row(sb, $"""
            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" class="card" style="background:{Card};border:1px solid {Line};border-radius:14px;">
            <tr><td class="pad" style="padding:22px 24px 8px;">
              <div style="font:800 11px/1 {Font};letter-spacing:1.4px;text-transform:uppercase;color:{Brand};">IELTS vocabulary</div>
              <div class="ink" style="margin:8px 0 4px;font:800 19px/1.3 {Font};color:{Ink};">{words.Count} words from today's news</div>
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0">{rows}</table>
            </td></tr></table>
            """);
        Spacer(sb, 24);
    }

    /// <summary>Örnek cümlede kelimeyi vurgular. Metin önce kodlanır, sonra işaretlenir (HTML enjeksiyonu yok).</summary>
    public static string Highlight(string example, string word)
    {
        var idx = example.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        if (idx < 0 || word.Length == 0) return E(example);
        // Kelimenin çekimli hâlini de kapsa: "halt" → "Halts", "curb" → "curbs" (kelime ortasından kesme).
        var end = idx + word.Length;
        while (end < example.Length && char.IsLetter(example[end])) end++;
        return E(example[..idx])
               + $"""<span class="hl" style="background:{Theme.Highlight};color:{Ink};font-style:normal;font-weight:700;padding:0 2px;border-radius:3px;">{E(example[idx..end])}</span>"""
               + E(example[end..]);
    }

    private static void Footer(StringBuilder sb, Digest digest, EmailLinks links)
    {
        string Button(string? href, string label, bool primary) => href is null ? "" :
            $"""<a href="{E(href)}" style="display:inline-block;margin:0 4px 8px;padding:11px 18px;border-radius:10px;font:700 14px {Font};{(primary ? $"background:{Brand};color:#ffffff;" : $"background:transparent;color:{Brand};border:1px solid {Brand};")}">{label}</a>""";

        var buttons = Button(links.Browser, "Read on the web", true) + Button(links.Archive, "Browse the archive", false);
        var repo = links.Repository is null ? "" : $"""&nbsp;·&nbsp;<a href="{E(links.Repository)}" class="mut" style="color:{Muted};text-decoration:underline;">Source on GitHub</a>""";
        var model = digest.Model is null ? "Turkish summaries unavailable today" : $"Summaries by {E(digest.Model)}";

        Row(sb, $"""
            <div style="text-align:center;padding:8px 0 0;">{buttons}</div>
            <p class="mut" style="margin:18px 0 0;text-align:center;font:500 12px/1.7 {Font};color:{Muted};">
              Curated from {digest.Stats.Feeds} feeds and {digest.Stats.Sources} outlets · {model}<br>
              Built with C# &amp; .NET by Yasin{repo}
            </p>
            """);
    }

    private static void Row(StringBuilder sb, string html) => sb.Append("<tr><td>").Append(html).Append("</td></tr>");
    private static void Spacer(StringBuilder sb, int px) => sb.Append($"""<tr><td style="height:{px}px;line-height:{px}px;font-size:1px;">&nbsp;</td></tr>""");
    private static string E(string? text) => WebUtility.HtmlEncode(text ?? "");

    public static string RenderText(Digest digest, DateTimeOffset now, EmailLinks links)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"TechDigest — {digest.Date:dddd, d MMMM yyyy}");
        if (links.Browser is not null) sb.AppendLine($"Read on the web: {links.Browser}");

        void Story(Story s)
        {
            sb.AppendLine($"\n* {s.Lead.Title}");
            sb.AppendLine($"  {s.Lead.Source} · {TimeText.Ago(s.Lead.Published, now)}{(s.Coverage > 1 ? $" · {s.Coverage} outlets" : "")}");
            var summary = s.All.Select(n => n.Summary).FirstOrDefault(x => x.Length > 0);
            if (summary is not null) sb.AppendLine($"  {TextTools.Truncate(summary, CardSummaryLength)}");
            if (s.TurkishSummary is not null) sb.AppendLine($"  TR: {s.TurkishSummary}");
            sb.AppendLine($"  {s.Lead.Link}");
        }

        if (digest.Headline is { } h)
        {
            sb.AppendLine("\n== TOP STORY ==");
            Story(h);
        }
        foreach (var section in digest.Sections)
        {
            sb.AppendLine($"\n== {section.Topic.Name.ToUpperInvariant()} ==");
            section.Stories.ForEach(Story);
        }
        if (digest.Vocabulary.Count > 0)
        {
            sb.AppendLine("\n== IELTS VOCABULARY ==");
            foreach (var w in digest.Vocabulary)
                sb.AppendLine($"\n* {w.Word} ({w.PartOfSpeech}) — {w.MeaningEn}\n  TR: {w.MeaningTr}\n  \"{w.Example}\"");
        }
        return sb.ToString();
    }
}
