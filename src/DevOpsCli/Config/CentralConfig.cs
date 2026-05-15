using System.Text.Json.Serialization;

namespace DevOpsCli.Config;

public sealed class CentralConfig
{
    [JsonPropertyName("defaultApiVersion")]
    public string DefaultApiVersion { get; set; } = "7.1";

    [JsonPropertyName("timeoutSeconds")]
    public int TimeoutSeconds { get; set; } = 30;

    [JsonPropertyName("organizations")]
    public Dictionary<string, OrgEntry> Organizations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class OrgEntry
{
    [JsonPropertyName("organizationUrl")]
    public string OrganizationUrl { get; set; } = "";

    [JsonPropertyName("pat")]
    public string Pat { get; set; } = "";

    [JsonPropertyName("defaultProject")]
    public string? DefaultProject { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("lastUpdated")]
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
