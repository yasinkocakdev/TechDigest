namespace TechDigest.Models;

/// <summary>
/// topics.json içindeki bir konu. <see cref="Strong"/> kelimeler tek başına konuyu belirler,
/// <see cref="Weak"/> kelimeler sadece puanı artırır (ör. "china" tek başına bir habere "Chips" demez).
/// </summary>
public sealed record Topic(string Id, string Name, string Color, List<string> Strong, List<string> Weak);

/// <summary>topics.json: konular ve seçim sınırları.</summary>
public sealed record TopicConfig(
    int MaxPerTopic,
    int MaxTotal,
    int CandidatesPerTopic,
    int MaxCandidates,
    List<string> ExcludePatterns,
    List<Topic> Topics);

/// <summary>publishers.json: yayıncı kalite kademeleri.</summary>
public sealed record PublisherConfig(List<string> Top, List<string> Trusted, List<string> Low, List<string> Blocked);

/// <summary>Uygulamanın tüm statik ayarları (config klasöründen).</summary>
public sealed record DigestConfig(List<FeedSource> Sources, TopicConfig Topics, PublisherConfig Publishers);
