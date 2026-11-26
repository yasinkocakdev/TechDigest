using Microsoft.Extensions.Logging;
using TechDigest.Configuration;
using TechDigest.Logging;
using TechDigest.Models;
using TechDigest.Services.Delivery;
using TechDigest.Services.Enrichment;
using TechDigest.Services.Fetching;
using TechDigest.Services.Ranking;
using TechDigest.Services.Rendering;

namespace TechDigest.Pipeline;

/// <summary>
/// Günlük bülteni baştan sona üretir:
/// <c>Fetch → Filter → Cluster → Rank → Enrich (Gemini, opsiyonel) → Compose → Render → Publish</c>.
/// Her adım ayrı bir sınıftır; bu sınıf sadece sırayı ve log'u yönetir. Bağımlılıklar dışarıdan
/// verildiği için uçtan uca test edilebilir (sahte HTTP, sabit saat).
/// </summary>
public sealed class DigestPipeline(
    DigestConfig config,
    AppOptions options,
    FeedFetcher fetcher,
    DigestEnricher? enricher,
    EmailSender? sender,
    ILogger<DigestPipeline> log,
    Func<DateTimeOffset>? clock = null)
{
    private readonly PublisherDirectory _publishers = new(config.Publishers);

    public async Task<Digest> RunAsync(CancellationToken ct = default)
    {
        var now = (clock ?? (() => DateTimeOffset.UtcNow))();
        var localNow = TimeZoneInfo.ConvertTime(now, options.TimeZone);
        var ranker = new StoryRanker(config.Topics, _publishers);

        FetchResult fetched;
        using (LogSetup.Step("1/5 Haberler çekiliyor"))
        {
            fetched = await fetcher.FetchAllAsync(config.Sources, now - options.Window, ct);
            log.LogInformation("{Feeds}/{Total} kaynak, son {Hours} saatte {Count} haber.",
                fetched.FeedsOk, config.Sources.Count, options.Window.TotalHours, fetched.Items.Count);
        }

        List<Story> candidates;
        using (LogSetup.Step("2/5 Seçim: filtre, kümeleme, puanlama"))
        {
            var filter = new ContentFilter(config.Topics, _publishers);
            var allowed = fetched.Items.Where(filter.Allows).ToList();
            var clusters = new StoryClusterer(_publishers).Cluster(allowed);
            var ranked = ranker.Rank(clusters, now);
            candidates = ranker.Candidates(ranked);

            log.LogInformation("{Filtered} haber elendi (engelli kaynak / tık tuzağı). {Clusters} olay, {Ranked} tanesi bir konuya uyuyor, {Candidates} aday.",
                fetched.Items.Count - allowed.Count, clusters.Count, ranked.Count, candidates.Count);
            foreach (var story in candidates.Take(10)) log.LogDebug("{Story}", story);
        }

        Enrichment? enrichment = null;
        using (LogSetup.Step("3/5 Gemini: Türkçe özet, anlamsal kopyalar, IELTS kelimeleri"))
        {
            if (enricher is null)
            {
                log.LogInformation("GEMINI_API_KEY yok, bu adım atlandı.");
            }
            else
            {
                enrichment = await enricher.EnrichAsync(candidates, ct);
                if (enrichment is null)
                {
                    log.LogWarning("Gemini sonucu alınamadı; bülten Türkçe özetsiz gidecek.");
                }
                else
                {
                    var before = candidates.Count;
                    candidates = EnrichmentMerger.Apply(enrichment, candidates);
                    foreach (var story in candidates) story.Score = ranker.Score(story, now);
                    log.LogInformation("{Model}: {Summaries} özet, {Merged} kopya birleştirildi, {Words} kelime.",
                        enrichment.Model, enrichment.Summaries.Count, before - candidates.Count, enrichment.Vocabulary.Count);
                }
            }
        }

        var (headline, sections) = ranker.Compose(candidates);
        var finalStories = sections.SelectMany(s => s.Stories).Prepend(headline).OfType<Story>().ToList();
        var digest = new Digest(
            Date: DateOnly.FromDateTime(localNow.DateTime),
            GeneratedAt: now,
            Headline: headline,
            Sections: sections,
            Vocabulary: enrichment?.Vocabulary ?? [],
            Stats: new DigestStats(
                Fetched: fetched.Items.Count,
                Stories: finalStories.Count,
                Sources: finalStories.SelectMany(s => s.All).Select(n => n.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                Feeds: fetched.FeedsOk,
                FeedsFailed: fetched.FeedsFailed),
            Model: enrichment?.Model);

        EmailMessage email;
        using (LogSetup.Step("4/5 Çıktılar"))
        {
            var links = new EmailLinks(options.DayUrl(digest.Date), options.ArchiveUrl, options.RepositoryUrl);
            email = new EmailMessage(
                EmailRenderer.Subject(digest),
                EmailRenderer.RenderHtml(digest, now, links),
                EmailRenderer.RenderText(digest, now, links));

            Directory.CreateDirectory(options.OutputDir);
            await File.WriteAllTextAsync(Path.Combine(options.OutputDir, "email.html"), email.Html, ct);
            await File.WriteAllTextAsync(Path.Combine(options.OutputDir, "email.txt"), email.Text, ct);
            await File.WriteAllTextAsync(Path.Combine(options.OutputDir, "digest.json"), DigestJson.Serialize(digest), ct);
            log.LogInformation("Manşet: {Headline}", headline?.Lead.Title ?? "(yok)");
            log.LogInformation("{Stories} haber, {Sections} konu → {Dir}/", digest.StoryCount, sections.Count, Path.GetFullPath(options.OutputDir));

            if (options.ArchiveDir is not null && !options.DryRun)
            {
                DigestJson.WriteArchive(options.ArchiveDir, digest);
                log.LogInformation("Arşive yazıldı: {Dir}/days/{Date:yyyy-MM-dd}.json", options.ArchiveDir, digest.Date);
            }
        }

        using (LogSetup.Step("5/5 Gönderim"))
        {
            if (options.DryRun || sender is null)
            {
                log.LogInformation("Dry run: mail gönderilmedi.");
            }
            else
            {
                await sender.SendAsync(options.Recipients!, email, ct);
                log.LogInformation("Mail gönderildi: \"{Subject}\"", email.Subject);
            }
        }

        return digest;
    }
}
