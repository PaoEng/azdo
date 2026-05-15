using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using DevOpsCli.AzureDevOps;
using DevOpsCli.Config;

namespace DevOpsCli.Tests;

public class AzureDevOpsClientTests : IDisposable
{
    private const string TestPat = "mySecretPAT";
    private const string TestOrgUrl = "https://dev.azure.com/testorg";

    private readonly AzureDevOpsClient _client;

    public AzureDevOpsClientTests()
    {
        var org = new OrgEntry { OrganizationUrl = TestOrgUrl, Pat = TestPat };
        _client = new AzureDevOpsClient(org, "7.1", 30);
    }

    public void Dispose() => _client.Dispose();

    [Fact]
    public void Constructor_SetsBasicAuthHeader_WithColonPrefixedPat()
    {
        var http = GetHttp(_client);
        var auth = http.DefaultRequestHeaders.Authorization;

        Assert.NotNull(auth);
        Assert.Equal("Basic", auth.Scheme);

        var expected = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{TestPat}"));
        Assert.Equal(expected, auth.Parameter);
    }

    [Fact]
    public void Constructor_AuthToken_UsesAsciiEncoding()
    {
        // Explicit check: base64 of ASCII(":PAT") — not UTF-8, not UTF-16
        var expectedToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{TestPat}"));

        var http = GetHttp(_client);
        Assert.Equal(expectedToken, http.DefaultRequestHeaders.Authorization?.Parameter);
    }

    [Fact]
    public void Constructor_AuthToken_HasNoTrailingNewline()
    {
        var token = GetHttp(_client).DefaultRequestHeaders.Authorization?.Parameter;

        Assert.NotNull(token);
        Assert.DoesNotContain('\n', token);
        Assert.DoesNotContain('\r', token);
    }

    [Fact]
    public void OrgUrl_TrimsTrailingSlash()
    {
        using var c = new AzureDevOpsClient(
            new OrgEntry { OrganizationUrl = "https://dev.azure.com/myorg/", Pat = "p" }, "7.1", 30);

        Assert.Equal("https://dev.azure.com/myorg", c.OrgUrl);
    }

    [Fact]
    public void OrgUrl_WithoutTrailingSlash_Unchanged()
    {
        Assert.Equal(TestOrgUrl, _client.OrgUrl);
    }

    [Fact]
    public void Constructor_SetsJsonAcceptHeader()
    {
        var http = GetHttp(_client);
        var hasJson = http.DefaultRequestHeaders.Accept
            .Any(h => h.MediaType == "application/json");
        Assert.True(hasJson, "HttpClient should accept application/json");
    }

    private static HttpClient GetHttp(AzureDevOpsClient client)
    {
        var field = typeof(AzureDevOpsClient)
            .GetField("_http", BindingFlags.NonPublic | BindingFlags.Instance);
        return (HttpClient)field!.GetValue(client)!;
    }
}
