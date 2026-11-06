using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CMPDriverTool.Core.Models;

public class DriverManifestItem
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("branch")]
    public string Branch { get; set; } = "Game Ready";

    [JsonPropertyName("releaseDate")]
    public string ReleaseDate { get; set; } = string.Empty;

    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("sizeMb")]
    public double SizeMb { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("isRecommended")]
    public bool IsRecommended { get; set; }

    [JsonPropertyName("supportedCards")]
    public List<string> SupportedCards { get; set; } = new() { "CMP 30HX", "CMP 40HX" };

    public string DisplayTitle => IsRecommended
        ? $"{Name} v{Version} (Рекомендуется)"
        : $"{Name} v{Version}";
}

public class DriverManifestRoot
{
    [JsonPropertyName("lastUpdated")]
    public string LastUpdated { get; set; } = string.Empty;

    [JsonPropertyName("drivers")]
    public List<DriverManifestItem> Drivers { get; set; } = new();
}
