using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace TechDigest.Text;

/// <summary>Metin temizleme, güvenli URL ve başlık karşılaştırma için küçük, saf (yan etkisiz) yardımcılar.</summary>
public static partial class TextTools
{
    /// <summary>HTML etiketlerini ve &amp;amp; gibi kodlamaları temizler, boşlukları tekilleştirir.</summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var noTags = HtmlTag().Replace(text, " ");
        return Whitespace().Replace(WebUtility.HtmlDecode(noTags), " ").Trim();
    }

    /// <summary>
    /// Dışarıdan gelen metni (RSS, Gemini) mailde/sitede göstermeye hazırlar: kontrol karakterlerini atar,
    /// boşlukları düzeltir, uzunluğu sınırlar.
    /// </summary>
    public static string Sanitize(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
            if (!char.IsControl(c) || c == ' ') sb.Append(c);
            else sb.Append(' ');
        return Truncate(Whitespace().Replace(sb.ToString(), " ").Trim(), maxLength);
    }

    /// <summary>Kelime ortasından kesmeden kısaltır ve "…" ekler.</summary>
    public static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        var cut = text[..maxLength];
        var lastSpace = cut.LastIndexOf(' ');
        if (lastSpace > maxLength * 0.6) cut = cut[..lastSpace];
        return cut.TrimEnd(' ', ',', ';', ':', '-', '—') + "…";
    }

    /// <summary>
    /// Sadece http/https linklerine izin verir. RSS'ten gelen "javascript:" veya "data:" linkleri
    /// mail/sitede tıklanabilir olmamalı.
    /// </summary>
    public static string? SafeUrl(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri.AbsoluteUri
            : null;

    private static readonly HashSet<string> StopWords =
    [
        "the", "a", "an", "of", "to", "in", "on", "for", "and", "or", "is", "are", "as", "at", "by", "with", "after",
        "its", "it", "from", "says", "said", "new", "over", "into", "amid", "will", "be", "has", "have", "this", "that",
        "how", "why", "what", "about", "more", "than", "up", "out", "but", "not", "can", "could", "would", "may", "report",
    ];

    /// <summary>Başlığı karşılaştırma için anlamlı kelime kümesine çevirir ("exports" ≈ "export").</summary>
    public static HashSet<string> Words(string title) =>
        WordPattern().Matches(title.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(w => w.Length > 1 && !StopWords.Contains(w))
            .Select(Stem)
            .ToHashSet();

    /// <summary>Çok kaba kök bulma: sondaki çoğul "s" atılır.</summary>
    private static string Stem(string w) => w.Length > 4 && w.EndsWith('s') && !w.EndsWith("ss") ? w[..^1] : w;

    /// <summary>
    /// İki başlık aynı olayı mı anlatıyor? Ya genel benzerlik (Jaccard) yüksek ya da
    /// kısa başlığın kelimelerinin çoğu (en az 4 ortak kelime) uzun başlıkta geçiyor.
    /// </summary>
    public static bool IsSameStory(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return false;
        var shared = a.Count(b.Contains);
        var jaccard = (double)shared / (a.Count + b.Count - shared);
        if (jaccard >= 0.6) return true;
        return shared >= 4 && (double)shared / Math.Min(a.Count, b.Count) >= 0.6;
    }

    /// <summary>Yayıncı adlarını karşılaştırmak için normalleştirir: "The Wall Street Journal" → "wall street journal".</summary>
    public static string NormalizePublisher(string name)
    {
        var s = name.Trim().ToLowerInvariant();
        if (s.StartsWith("the ")) s = s[4..];
        if (s.EndsWith(".com")) s = s[..^4];
        return Whitespace().Replace(NonWord().Replace(s, " "), " ").Trim();
    }

    /// <summary>Gösterim için yayıncı adı: "Bloomberg.com" → "Bloomberg".</summary>
    public static string DisplayPublisher(string name)
    {
        // Google News bazen yayıncı yerine adres yazıyor: "https://www.example.com/" → "example.com"
        if (Uri.TryCreate(name, UriKind.Absolute, out var uri) && uri.Scheme.StartsWith("http"))
            name = uri.Host.StartsWith("www.") ? uri.Host[4..] : uri.Host;
        return name.EndsWith(".com", StringComparison.OrdinalIgnoreCase) && name.Length > 4 ? name[..^4] : name;
    }

    [GeneratedRegex("<[^>]+>")] private static partial Regex HtmlTag();
    [GeneratedRegex(@"\s+")] private static partial Regex Whitespace();
    [GeneratedRegex(@"[\p{L}\p{N}]+")] private static partial Regex WordPattern();
    [GeneratedRegex(@"[^\p{L}\p{N}&/]+")] private static partial Regex NonWord();
}
