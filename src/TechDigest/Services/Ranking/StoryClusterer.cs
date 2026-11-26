using TechDigest.Models;
using TechDigest.Text;

namespace TechDigest.Services.Ranking;

/// <summary>
/// Aynı olayı anlatan haberleri tek bir <see cref="Story"/> altında toplar.
/// Eski sürüm kopyaları atıyordu; şimdi tutuyoruz çünkü "kaç yayıncı bu haberi verdi"
/// önemin en güçlü sinyali. Kümenin lideri en kaliteli yayıncıdan, eşitlikte açıklaması olan haberdir.
/// </summary>
public sealed class StoryClusterer(PublisherDirectory publishers)
{
    public List<Story> Cluster(IEnumerable<NewsItem> items)
    {
        // Önce kaliteli kaynaklar: küme liderleri doğal olarak onlar olur.
        var ordered = items
            .OrderByDescending(i => publishers.TierOf(i.Source))
            .ThenByDescending(i => i.Summary.Length > 0)
            .ThenByDescending(i => i.Published);

        var clusters = new List<(Story Story, List<HashSet<string>> Words)>();
        foreach (var item in ordered)
        {
            var words = TextTools.Words(item.Title);
            if (words.Count == 0) continue;

            var match = clusters.FirstOrDefault(c =>
                c.Story.All.Any(n => n.Link == item.Link) || c.Words.Any(w => TextTools.IsSameStory(w, words)));

            if (match.Story is null)
            {
                clusters.Add((new Story(item), [words]));
            }
            else if (!match.Story.All.Any(n => n.Link == item.Link))
            {
                match.Story.Related.Add(item);
                match.Words.Add(words);
            }
        }

        var merged = MergeByRareWords(clusters);
        foreach (var story in merged) PickBestLead(story);
        return merged;
    }

    /// <summary>
    /// İkinci geçiş: farklı kelimelerle yazılmış aynı olayı yakalar ("FIFA says hackers accessed data" ≈
    /// "FIFA hit by cyberattack, data stolen"). Her kelimeye o günkü nadirliğine göre ağırlık verilir (IDF):
    /// "fifa" gibi sadece o olayda geçen kelimeler çok, "nvidia" gibi her yerde geçenler az sayılır.
    /// İki küme, üyelerinden herhangi bir başlık çifti <see cref="IsSameEvent"/> diyorsa birleşir.
    /// Kümelerin kelime havuzları birleştirilip karşılaştırılmaz: büyük küme her şeyle örtüşür ve
    /// alakasız haberleri yutar.
    /// </summary>
    private static List<Story> MergeByRareWords(List<(Story Story, List<HashSet<string>> Words)> clusters)
    {
        var df = new Dictionary<string, int>();
        var titles = 0;
        foreach (var words in clusters.SelectMany(c => c.Words))
        {
            titles++;
            foreach (var w in words) df[w] = df.GetValueOrDefault(w) + 1;
        }
        double Weight(string w) => Math.Log((double)(titles + 1) / df[w]);

        // Kümeler büyüdükçe yeni eşleşmeler doğar (A+B birleşince C'ye benzeyebilir); değişiklik kalmayana
        // kadar tekrar edilir. Pratikte 2-3 tur sürer.
        var alive = Enumerable.Repeat(true, clusters.Count).ToArray();
        bool changed;
        do
        {
            changed = false;
            for (var i = 0; i < clusters.Count; i++)
            {
                if (!alive[i]) continue;
                for (var j = i + 1; j < clusters.Count; j++)
                {
                    if (!alive[j]) continue;
                    var same = clusters[i].Words.Any(a => clusters[j].Words.Any(b => IsSameEvent(a, b, Weight)));
                    if (!same) continue;
                    clusters[i].Story.Absorb(clusters[j].Story);
                    clusters[i].Words.AddRange(clusters[j].Words);
                    alive[j] = false;
                    changed = true;
                }
            }
        } while (changed);
        return clusters.Where((_, i) => alive[i]).Select(c => c.Story).ToList();
    }

    public static bool IsSameEvent(HashSet<string> a, HashSet<string> b, Func<string, double> weight)
    {
        var shared = a.Where(b.Contains).ToList();
        if (shared.Count < 2) return false;
        var sharedWeight = shared.Sum(weight);
        var smaller = Math.Min(a.Sum(weight), b.Sum(weight));
        if (smaller <= 0) return false;
        var ratio = sharedWeight / smaller;
        // İki yol: (a) en az bir ortak kelime gerçekten nadir (özel isim gibi) ya da (b) üç ve daha fazla
        // ortak kelime ve daha yüksek oran. Sadece "ai model chip" gibi genel kelimeler yetmez.
        return (ratio >= 0.45 && shared.Max(weight) >= 3.0) || (shared.Count >= 3 && ratio >= 0.55);
    }

    /// <summary>Lider: en iyi kademe → açıklaması olan → en yeni.</summary>
    private void PickBestLead(Story story)
    {
        var best = story.All
            .OrderByDescending(n => publishers.TierOf(n.Source))
            .ThenByDescending(n => n.Summary.Length > 0)
            .ThenByDescending(n => n.Published)
            .First();
        story.PromoteLead(best);
    }
}
