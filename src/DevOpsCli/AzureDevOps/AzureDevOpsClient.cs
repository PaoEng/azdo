using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DevOpsCli.Config;

namespace DevOpsCli.AzureDevOps;

public sealed class AzureDevOpsClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _apiVersion;

    public string OrgUrl => _baseUrl;

    public AzureDevOpsClient(OrgEntry org, string apiVersion, int timeoutSec)
    {
        _baseUrl = org.OrganizationUrl.TrimEnd('/');
        _apiVersion = apiVersion;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSec) };

        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{org.Pat}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<JsonElement> GetAsync(string path, CancellationToken ct = default)
    {
        var url = BuildUrl(path);
        using var resp = await _http.GetAsync(url, ct);
        return await ReadJsonAsync(resp, ct);
    }

    public async Task<JsonElement> PostAsync(string path, HttpContent body, CancellationToken ct = default)
    {
        var url = BuildUrl(path);
        using var resp = await _http.PostAsync(url, body, ct);
        return await ReadJsonAsync(resp, ct);
    }

    public async Task<JsonElement> PatchAsync(string path, HttpContent body, CancellationToken ct = default)
    {
        var url = BuildUrl(path);
        var req = new HttpRequestMessage(HttpMethod.Patch, url) { Content = body };
        using var resp = await _http.SendAsync(req, ct);
        return await ReadJsonAsync(resp, ct);
    }

    public static StringContent JsonBody(object obj, string mediaType = "application/json") =>
        new(JsonSerializer.Serialize(obj), Encoding.UTF8, mediaType);

    public static StringContent JsonPatchBody(object obj) =>
        new(JsonSerializer.Serialize(obj), Encoding.UTF8, "application/json-patch+json");

    private string BuildUrl(string path)
    {
        var sep = path.Contains('?') ? "&" : "?";
        return $"{_baseUrl}/{path.TrimStart('/')}{sep}api-version={_apiVersion}";
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        var content = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Azure DevOps API {(int)resp.StatusCode} {resp.ReasonPhrase}: {Truncate(content, 500)}");
        if (string.IsNullOrWhiteSpace(content)) return default;
        using var doc = JsonDocument.Parse(content);
        return doc.RootElement.Clone();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    public void Dispose() => _http.Dispose();
}
