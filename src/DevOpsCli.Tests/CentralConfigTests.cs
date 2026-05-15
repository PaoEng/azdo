using System.Text.Json;
using DevOpsCli.Config;

namespace DevOpsCli.Tests;

public class CentralConfigTests
{
    [Fact]
    public void Organizations_CaseInsensitiveLookup_ReturnsEntry()
    {
        var cfg = new CentralConfig();
        cfg.Organizations["MyOrg"] = new OrgEntry { Pat = "p1" };

        Assert.True(cfg.Organizations.ContainsKey("myorg"));
        Assert.True(cfg.Organizations.ContainsKey("MYORG"));
        Assert.True(cfg.Organizations.ContainsKey("MyOrg"));
        Assert.Equal("p1", cfg.Organizations["myorg"].Pat);
    }

    [Fact]
    public void Serialize_RoundTrip_ProducesStableJson()
    {
        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var cfg = new CentralConfig { DefaultApiVersion = "7.1", TimeoutSeconds = 30 };
        cfg.Organizations["testorg"] = new OrgEntry
        {
            OrganizationUrl = "https://dev.azure.com/testorg",
            Pat = "testpat",
            DefaultProject = "proj1"
        };

        var json1 = JsonSerializer.Serialize(cfg, opts);
        var cfg2 = JsonSerializer.Deserialize<CentralConfig>(json1, opts)!;
        var json2 = JsonSerializer.Serialize(cfg2, opts);

        Assert.Equal(json1, json2);
    }

    [Fact]
    public void Deserialize_JsonPropertyNames_MappedCorrectly()
    {
        const string json = """
            {
              "defaultApiVersion": "6.0",
              "timeoutSeconds": 45,
              "organizations": {
                "myorg": {
                  "organizationUrl": "https://dev.azure.com/myorg",
                  "pat": "secret",
                  "defaultProject": "proj"
                }
              }
            }
            """;

        var cfg = JsonSerializer.Deserialize<CentralConfig>(json,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;

        Assert.Equal("6.0", cfg.DefaultApiVersion);
        Assert.Equal(45, cfg.TimeoutSeconds);
        Assert.Equal("secret", cfg.Organizations["myorg"].Pat);
        Assert.Equal("proj", cfg.Organizations["myorg"].DefaultProject);
    }

    [Fact]
    public void OrgEntry_DefaultValues_AreSet()
    {
        var entry = new OrgEntry();
        Assert.Equal("", entry.OrganizationUrl);
        Assert.Equal("", entry.Pat);
        Assert.Null(entry.DefaultProject);
        Assert.Null(entry.Description);
    }
}
