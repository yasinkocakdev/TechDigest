using System.Text.Json;
using TechDigest.Models;
using TechDigest.Services;

// Adım 1: kaynakları oku → RSS'leri çek → son 24 saatin başlıklarını yazdır.
var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var configDir = Path.Combine(AppContext.BaseDirectory, "config");

var sources = JsonSerializer.Deserialize<List<FeedSource>>(
    File.ReadAllText(Path.Combine(configDir, "sources.json")), jsonOptions)!;

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
// Bazı siteler User-Agent olmayan istekleri reddediyor.
http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; TechDigest/1.0)");

Console.WriteLine($"{sources.Count} kaynak çekiliyor...");
var items = await new FeedFetcher(http).FetchAllAsync(sources, TimeSpan.FromHours(24));

Console.WriteLine($"\nSon 24 saatte {items.Count} haber:\n");
foreach (var item in items)
    Console.WriteLine($"[{item.Source}] {item.Title}");
