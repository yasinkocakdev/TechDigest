using TechDigest.Models;
using TechDigest.Text;

namespace TechDigest.Services.Ranking;

public enum PublisherTier { Blocked = -1, Low = 0, Normal = 1, Trusted = 2, Top = 3 }

/// <summary>publishers.json'daki listelere göre bir yayıncının kalite kademesini söyler.</summary>
public sealed class PublisherDirectory
{
    private readonly Dictionary<string, PublisherTier> _tiers = new();

    public PublisherDirectory(PublisherConfig config)
    {
        // Sıra önemli: aynı isim iki listede varsa daha kısıtlayıcı olan kazanır.
        Add(config.Top, PublisherTier.Top);
        Add(config.Trusted, PublisherTier.Trusted);
        Add(config.Low, PublisherTier.Low);
        Add(config.Blocked, PublisherTier.Blocked);
    }

    private void Add(IEnumerable<string> names, PublisherTier tier)
    {
        foreach (var name in names) _tiers[TextTools.NormalizePublisher(name)] = tier;
    }

    public PublisherTier TierOf(string publisher) =>
        _tiers.TryGetValue(TextTools.NormalizePublisher(publisher), out var tier) ? tier : PublisherTier.Normal;

    public bool IsBlocked(string publisher) => TierOf(publisher) == PublisherTier.Blocked;
}
