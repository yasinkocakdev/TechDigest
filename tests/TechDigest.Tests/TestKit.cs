using System.Net;
using System.Text;
using TechDigest.Configuration;
using TechDigest.Models;
using TechDigest.Services.Ranking;

namespace TechDigest.Tests;

/// <summary>Testlerde ortak kullanılan sabit saat, örnek veri ve sahte HTTP.</summary>
public static class TestKit
{
    public static readonly DateTimeOffset Now = new(2026, 11, 27, 4, 0, 0, TimeSpan.Zero); // 07:00 İstanbul

    public static NewsItem News(string title, string source = "Reuters", string summary = "", double hoursAgo = 2, string? link = null) =>
        new(title, summary, link ?? $"https://example.com/{Uri.EscapeDataString(title)}/{source.Replace(' ', '-')}", source, Now.AddHours(-hoursAgo));

    public static DigestConfig Config() => ConfigLoader.Load(Path.Combine(AppContext.BaseDirectory, "config"));

    public static PublisherDirectory Publishers() => new(Config().Publishers);

    public static Topic Topic(string id = "chips", params string[] strong) =>
        new(id, id.ToUpperInvariant(), "#0891b2", strong.Length > 0 ? [.. strong] : ["nvidia", "chip"], ["china"]);

    /// <summary>RSS 2.0 belgesi üretir.</summary>
    public static string Rss(params (string Title, string Link, string Description, DateTimeOffset Date)[] items)
    {
        var sb = new StringBuilder("""<?xml version="1.0" encoding="UTF-8"?><rss version="2.0"><channel><title>T</title><link>https://example.com</link><description>D</description>""");
        foreach (var i in items)
            sb.Append($"<item><title>{WebUtility.HtmlEncode(i.Title)}</title><link>{WebUtility.HtmlEncode(i.Link)}</link>" +
                      $"<description>{WebUtility.HtmlEncode(i.Description)}</description><pubDate>{i.Date:R}</pubDate></item>");
        return sb.Append("</channel></rss>").ToString();
    }
}

/// <summary>İstekleri bir fonksiyona yönlendiren sahte HTTP; gerçek ağa çıkmaz.</summary>
public sealed class FakeHttp(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];
    public List<string> Bodies { get; } = [];

    public FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> handler) : this(r => Task.FromResult(handler(r))) { }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
        return await handler(request);
    }

    public HttpClient Client() => new(this) { Timeout = Timeout.InfiniteTimeSpan };

    public static HttpResponseMessage Text(string body, HttpStatusCode status = HttpStatusCode.OK, string type = "application/xml") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, type) };
}
