using System.Net;
using System.Text;
using TechDigest.Models;

namespace TechDigest.Services;

/// <summary>
/// Konulara ayrılmış haberlerden HTML ve düz metin mail gövdesi üretir.
/// Mail istemcileri harici CSS'i desteklemediği için stiller satır içi (inline) yazılır.
/// </summary>
public static class EmailRenderer
{
    private const int MaxSummaryLength = 280;

    public static string Subject(DateTimeOffset date, int count) =>
        $"🗞 TechDigest — {date:dd MMM yyyy} ({count} stories)";

    public static string RenderHtml(IReadOnlyList<DigestSection> sections, DateTimeOffset date)
    {
        var sb = new StringBuilder();
        sb.Append("""
            <!doctype html><html><body style="margin:0;padding:0;background:#f4f4f5;">
            <div style="max-width:640px;margin:0 auto;padding:24px 16px;font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:#18181b;">
            """);
        sb.Append($"""<h1 style="font-size:22px;margin:0 0 4px;">🗞 TechDigest</h1>""");
        sb.Append($"""<p style="margin:0 0 24px;color:#71717a;font-size:14px;">{date:dddd, dd MMMM yyyy} · AI, chips, markets &amp; software</p>""");

        if (sections.Count == 0)
            sb.Append("""<p>No matching stories in the last 24 hours.</p>""");

        foreach (var section in sections)
        {
            sb.Append($"""<h2 style="font-size:17px;margin:28px 0 12px;padding-bottom:6px;border-bottom:2px solid #e4e4e7;">{section.Topic.Emoji} {E(section.Topic.Name)}</h2>""");
            foreach (var d in section.Items)
            {
                var n = d.News;
                sb.Append("""<div style="background:#fff;border-radius:8px;padding:14px 16px;margin-bottom:10px;">""");
                sb.Append($"""<a href="{E(n.Link)}" style="font-size:16px;font-weight:600;color:#1d4ed8;text-decoration:none;line-height:1.35;">{E(n.Title)}</a>""");
                sb.Append($"""<div style="font-size:12px;color:#71717a;margin:4px 0 8px;">{E(n.Source)} · {n.Published.ToUniversalTime():HH:mm} UTC</div>""");
                if (n.Summary.Length > 0)
                    sb.Append($"""<p style="margin:0 0 8px;font-size:14px;line-height:1.5;color:#3f3f46;">{E(Truncate(n.Summary))}</p>""");
                if (!string.IsNullOrWhiteSpace(d.TurkishSummary))
                    sb.Append($"""<p style="margin:0;font-size:13px;line-height:1.5;color:#52525b;background:#fafafa;border-left:3px solid #ef4444;padding:6px 10px;">🇹🇷 {E(d.TurkishSummary)}</p>""");
                sb.Append("</div>");
            }
        }

        sb.Append("""<p style="margin-top:32px;font-size:12px;color:#a1a1aa;">Sent by TechDigest · github.com/yasinkocakdev/TechDigest</p></div></body></html>""");
        return sb.ToString();
    }

    public static string RenderText(IReadOnlyList<DigestSection> sections, DateTimeOffset date)
    {
        var sb = new StringBuilder($"TechDigest — {date:dd MMM yyyy}\n");
        foreach (var section in sections)
        {
            sb.Append($"\n== {section.Topic.Name} ==\n");
            foreach (var d in section.Items)
            {
                sb.Append($"\n* {d.News.Title} [{d.News.Source}]\n");
                if (d.News.Summary.Length > 0) sb.Append($"  {Truncate(d.News.Summary)}\n");
                if (!string.IsNullOrWhiteSpace(d.TurkishSummary)) sb.Append($"  TR: {d.TurkishSummary}\n");
                sb.Append($"  {d.News.Link}\n");
            }
        }
        return sb.ToString();
    }

    public static string Truncate(string text) =>
        text.Length <= MaxSummaryLength ? text : text[..MaxSummaryLength].TrimEnd() + "…";

    private static string E(string text) => WebUtility.HtmlEncode(text);
}
