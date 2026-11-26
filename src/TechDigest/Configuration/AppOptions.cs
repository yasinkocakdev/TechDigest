using System.Text.Json;
using TechDigest.Models;

namespace TechDigest.Configuration;

/// <summary>
/// Çalışma ayarları: komut satırı + ortam değişkenleri, tek yerde okunur ve doğrulanır.
/// Eksik bir secret, iş yarıda kalmadan en başta açık bir mesajla bildirilir.
/// </summary>
public sealed record AppOptions
{
    public bool DryRun { get; init; }
    public bool Verbose { get; init; }
    public string OutputDir { get; init; } = "out";
    public string? ArchiveDir { get; init; }

    public string? GmailAddress { get; init; }
    public string? GmailAppPassword { get; init; }
    public string? Recipients { get; init; }

    public string? GeminiApiKey { get; init; }
    public string? GeminiModel { get; init; }

    public string? SiteUrl { get; init; }
    public string? RepositoryUrl { get; init; }
    public TimeZoneInfo TimeZone { get; init; } = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
    public TimeSpan Window { get; init; } = TimeSpan.FromHours(24);

    public static AppOptions FromEnvironment(string[] args, Func<string, string?>? env = null)
    {
        env ??= Environment.GetEnvironmentVariable;
        string? Get(string name) => env(name) is { Length: > 0 } v ? v.Trim() : null;

        // GitHub Actions'ta repo adından site ve repo adresleri türetilir: owner/Repo → owner.github.io/Repo
        var repo = Get("GITHUB_REPOSITORY");
        var derivedSite = repo is null ? null : $"https://{repo.Split('/')[0].ToLowerInvariant()}.github.io/{repo.Split('/')[1]}/";
        var derivedRepo = repo is null ? null : $"https://github.com/{repo}";

        return new AppOptions
        {
            DryRun = args.Contains("--dry-run"),
            Verbose = args.Contains("--verbose") || Get("RUNNER_DEBUG") == "1",
            OutputDir = Get("OUTPUT_DIR") ?? "out",
            ArchiveDir = Get("ARCHIVE_DIR"),
            GmailAddress = Get("GMAIL_ADDRESS"),
            GmailAppPassword = Get("GMAIL_APP_PASSWORD"),
            Recipients = Get("TO_ADDRESS"),
            GeminiApiKey = Get("GEMINI_API_KEY"),
            GeminiModel = Get("GEMINI_MODEL"),
            SiteUrl = Get("SITE_URL") ?? derivedSite,
            RepositoryUrl = derivedRepo,
        };
    }

    /// <summary>Gönderim yapılacaksa mail ayarlarının hepsi gerekli.</summary>
    public IReadOnlyList<string> Validate()
    {
        if (DryRun) return [];
        var missing = new List<string>();
        if (GmailAddress is null) missing.Add("GMAIL_ADDRESS");
        if (GmailAppPassword is null) missing.Add("GMAIL_APP_PASSWORD");
        if (Recipients is null) missing.Add("TO_ADDRESS");
        return missing;
    }

    public string? DayUrl(DateOnly date) => SiteUrl is null ? null : $"{SiteUrl.TrimEnd('/')}/#/{date:yyyy-MM-dd}";
    public string? ArchiveUrl => SiteUrl is null ? null : $"{SiteUrl.TrimEnd('/')}/#/archive";
}

/// <summary>config/ klasöründeki JSON dosyalarını okur ve temel tutarlılığı kontrol eder.</summary>
public static class ConfigLoader
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };

    public static DigestConfig Load(string directory)
    {
        T Read<T>(string file) =>
            JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(directory, file)), Json)
            ?? throw new InvalidDataException($"{file} boş ya da okunamadı.");

        var config = new DigestConfig(Read<List<FeedSource>>("sources.json"), Read<TopicConfig>("topics.json"), Read<PublisherConfig>("publishers.json"));

        var duplicateTopic = config.Topics.Topics.GroupBy(t => t.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicateTopic is not null) throw new InvalidDataException($"topics.json: '{duplicateTopic.Key}' id'si birden fazla kez geçiyor.");
        if (config.Topics.Topics.Any(t => t.Strong.Count == 0)) throw new InvalidDataException("topics.json: her konunun en az bir 'strong' kelimesi olmalı.");
        if (config.Sources.Count == 0) throw new InvalidDataException("sources.json: hiç kaynak yok.");
        return config;
    }
}
