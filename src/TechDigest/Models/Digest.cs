namespace TechDigest.Models;

/// <summary>topics.json içindeki bir konu.</summary>
public sealed record Topic(string Name, string Emoji, List<string> Keywords);

/// <summary>topics.json dosyasının tamamı.</summary>
public sealed record TopicConfig(int MaxPerTopic, int MaxTotal, List<Topic> Topics)
{
    /// <summary>Başlığında bunlardan biri geçen haber atılır (yatırım tavsiyesi tarzı içerik).</summary>
    public List<string> ExcludeKeywords { get; init; } = [];
}

/// <summary>Bir konuya atanmış, puanlanmış haber. TurkishSummary Gemini'den sonra dolar.</summary>
public sealed record DigestItem(NewsItem News, int Score)
{
    public string? TurkishSummary { get; set; }
}

/// <summary>Mailde bir başlık altında gösterilecek haberler.</summary>
public sealed record DigestSection(Topic Topic, List<DigestItem> Items);
