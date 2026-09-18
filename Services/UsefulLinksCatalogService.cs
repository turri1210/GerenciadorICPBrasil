using System.Text.Json;
using System.Text.Json.Serialization;
using GerenciadorIcpBrasil.Models;

namespace GerenciadorIcpBrasil.Services;

public sealed class UsefulLinksCatalogService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _catalogPath;

    public UsefulLinksCatalogService(string baseDirectory)
    {
        _catalogPath = Path.Combine(baseDirectory, "links-uteis.json");
    }

    public async Task<UsefulLinksLoadResult> LoadAsync()
    {
        if (!File.Exists(_catalogPath))
        {
            return new UsefulLinksLoadResult(Array.Empty<UsefulLinkItem>(), "local", null);
        }

        try
        {
            var json = await File.ReadAllTextAsync(_catalogPath).ConfigureAwait(false);
            var payload = JsonSerializer.Deserialize<UsefulLinksCatalog>(json, SerializerOptions);
            var links = payload?.Links?
                .Where(l => !string.IsNullOrWhiteSpace(l.Title) && !string.IsNullOrWhiteSpace(l.Url))
                .Select(l => new UsefulLinkItem
                {
                    Title = l.Title!.Trim(),
                    Description = l.Description?.Trim() ?? string.Empty,
                    Url = l.Url!.Trim(),
                    Category = l.Category?.Trim() ?? string.Empty,
                })
                .DistinctBy(l => l.Url, StringComparer.OrdinalIgnoreCase)
                .OrderBy(l => l.Category)
                .ThenBy(l => l.Title)
                .ToList()
                ?? new List<UsefulLinkItem>();

            return new UsefulLinksLoadResult(links, "local", payload?.UpdatedAt);
        }
        catch
        {
            return new UsefulLinksLoadResult(Array.Empty<UsefulLinkItem>(), "local", null);
        }
    }

    private sealed class UsefulLinksCatalog
    {
        [JsonPropertyName("updatedAt")]
        public string? UpdatedAt { get; set; }

        [JsonPropertyName("links")]
        public List<UsefulLinkDefinition> Links { get; set; } = new();
    }

    private sealed class UsefulLinkDefinition
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }
    }

}

public sealed record UsefulLinksLoadResult(IReadOnlyList<UsefulLinkItem> Links, string Source, string? UpdatedAt);
