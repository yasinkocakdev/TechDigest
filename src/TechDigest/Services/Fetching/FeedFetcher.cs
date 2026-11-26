using System.ServiceModel.Syndication;
using System.Xml;
using Microsoft.Extensions.Logging;
using TechDigest.Models;
using TechDigest.Text;

namespace TechDigest.Services.Fetching;

public sealed record FetchResult(List<NewsItem> Items, int FeedsOk, int FeedsFailed);

/// <summary>
/// RSS/Atom kaynaklarını paralel çeker ve son N saatteki haberleri döner.
/// Bir kaynak çökerse diğerleri etkilenmez. Güvenlik için cevap boyutu ve haber sayısı sınırlıdır,
/// XML'de DTD/harici kaynak çözümleme kapalıdır, sadece http(s) linkleri kabul edilir.
/// </summary>
public sealed class FeedFetcher(HttpClient http, ILogger<FeedFetcher> log)
{
    public const long MaxFeedBytes = 5 * 1024 * 1024;
    public const int MaxItemsPerFeed = 200;

    public async Task<FetchResult> FetchAllAsync(
        IReadOnlyList<FeedSource> sources, DateTimeOffset since, CancellationToken ct = default)
    {
        var results = await Task.WhenAll(sources.Select(s => FetchOneAsync(s, ct)));

        var items = results
            .SelectMany(r => r ?? [])
            .Where(i => i.Published >= since)
            .OrderByDescending(i => i.Published)
            .ToList();

        var failed = results.Count(r => r is null);
        return new FetchResult(items, sources.Count - failed, failed);
    }

    private async Task<List<NewsItem>?> FetchOneAsync(FeedSource source, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(source.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxFeedBytes)
                throw new InvalidDataException($"feed {MaxFeedBytes / 1024 / 1024} MB sınırını aşıyor");

            await using var raw = await response.Content.ReadAsStreamAsync(ct);
            await using var limited = new LimitedStream(raw, MaxFeedBytes);
            var items = Parse(limited, source);
            log.LogDebug("✓ {Feed}: {Count} haber", source.Name, items.Count);
            return items;
        }
        catch (Exception ex) when (ex is HttpRequestException or XmlException or TaskCanceledException or InvalidDataException)
        {
            log.LogWarning("Kaynak atlandı: {Feed} ({Reason})", source.Name, ex.Message);
            return null;
        }
    }

    /// <summary>Ağdan bağımsız olduğu için testlerde doğrudan çağrılabilir.</summary>
    public static List<NewsItem> Parse(Stream xml, FeedSource source)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, Async = false };
        using var reader = XmlReader.Create(xml, settings);
        var feed = SyndicationFeed.Load(reader);

        var items = new List<NewsItem>();
        foreach (var item in feed.Items.Take(MaxItemsPerFeed))
        {
            var rawTitle = TextTools.Clean(item.Title?.Text);
            var (title, publisher) = source.IsAggregator
                ? SplitPublisher(rawTitle, source.Name)
                : (rawTitle, source.Publisher!);

            var link = TextTools.SafeUrl(item.Links.FirstOrDefault()?.Uri?.ToString());
            if (title.Length == 0 || link is null) continue;

            var summary = TextTools.Clean(item.Summary?.Text);
            // Google News açıklaması sadece başlığın tekrarı; işe yaramıyor.
            if (summary.StartsWith(title, StringComparison.OrdinalIgnoreCase)) summary = "";

            items.Add(new NewsItem(
                Title: TextTools.Sanitize(title, 300),
                Summary: TextTools.Sanitize(summary, 600),
                Link: link,
                Source: TextTools.DisplayPublisher(TextTools.Sanitize(publisher, 80)),
                // Bazı kaynaklar sadece LastUpdatedTime doldurur.
                Published: item.PublishDate != default ? item.PublishDate : item.LastUpdatedTime));
        }
        return items;
    }

    /// <summary>Google News başlıkları "Nvidia hits record - Reuters" şeklinde: asıl yayıncıyı ayırır.</summary>
    public static (string Title, string Source) SplitPublisher(string title, string fallback)
    {
        var idx = title.LastIndexOf(" - ", StringComparison.Ordinal);
        return idx > 0 ? (title[..idx].Trim(), title[(idx + 3)..].Trim()) : (title, fallback);
    }

    /// <summary>Belirli bir bayttan fazlasını okutmayan sarmalayıcı (Content-Length yalan söylese bile).</summary>
    private sealed class LimitedStream(Stream inner, long limit) : Stream
    {
        private long _read;
        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = inner.Read(buffer, offset, count);
            _read += n;
            if (_read > limit) throw new InvalidDataException($"feed {limit / 1024 / 1024} MB sınırını aşıyor");
            return n;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}
