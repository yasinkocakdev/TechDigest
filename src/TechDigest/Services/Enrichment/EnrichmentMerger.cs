using TechDigest.Models;

namespace TechDigest.Services.Enrichment;

/// <summary>
/// Gemini çıktısını aday kümelere uygular: Türkçe özetleri yerleştirir ve Gemini'nin "aynı olay"
/// dediği kümeleri birleştirir (puanı yüksek olan kalır, diğerinin kaynakları ona eklenir).
/// </summary>
public static class EnrichmentMerger
{
    public static List<Story> Apply(Enrichment enrichment, IReadOnlyList<Story> candidates)
    {
        foreach (var (id, tr) in enrichment.Summaries) candidates[id].TurkishSummary = tr;

        var removed = new HashSet<int>();
        foreach (var group in enrichment.DuplicateGroups)
        {
            var alive = group.Where(id => !removed.Contains(id)).ToList();
            if (alive.Count < 2) continue;

            var keeper = alive.MaxBy(id => candidates[id].Score);
            foreach (var id in alive.Where(id => id != keeper))
            {
                candidates[keeper].Absorb(candidates[id]);
                removed.Add(id);
            }
        }

        return candidates.Where((_, i) => !removed.Contains(i)).ToList();
    }
}
