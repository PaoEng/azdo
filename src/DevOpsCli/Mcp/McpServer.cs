using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevOpsCli.Mcp;

public sealed class JsonRpcException : Exception
{
    public int Code { get; }
    public JsonRpcException(int code, string message) : base(message) { Code = code; }
}

public sealed class McpServer
{
    private const string ProtocolVersion = "2024-11-05";
    private const string ServerName = "azdo";
    private const string ServerVersion = "0.1.0";

    private static readonly JsonSerializerOptions WireOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task RunAsync(CancellationToken ct = default)
    {
        Console.Error.WriteLine($"[azdo-mcp] starting (protocol {ProtocolVersion})");

        var reader = Console.In;
        var writer = Console.Out;

        string? line;
        while (!ct.IsCancellationRequested && (line = await reader.ReadLineAsync(ct)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(line);
            }
            catch (JsonException ex)
            {
                Console.Error.WriteLine($"[azdo-mcp] parse error: {ex.Message}");
                continue;
            }

            using (doc)
            {
                var root = doc.RootElement;
                var method = root.TryGetProperty("method", out var m) ? m.GetString() : null;
                var hasId = root.TryGetProperty("id", out var idEl);
                var paramsEl = root.TryGetProperty("params", out var p) ? (JsonElement?)p : null;

                if (!hasId)
                {
                    if (method == "notifications/initialized")
                        Console.Error.WriteLine("[azdo-mcp] client initialized");
                    continue;
                }

                object response;
                try
                {
                    var result = await DispatchAsync(method, paramsEl, ct);
                    response = new { jsonrpc = "2.0", id = idEl, result };
                }
                catch (JsonRpcException jre)
                {
                    response = new { jsonrpc = "2.0", id = idEl, error = new { code = jre.Code, message = jre.Message } };
                }
                catch (Exception ex)
                {
                    response = new { jsonrpc = "2.0", id = idEl, error = new { code = -32603, message = ex.Message } };
                }

                await writer.WriteLineAsync(JsonSerializer.Serialize(response, WireOpts));
                await writer.FlushAsync(ct);
            }
        }

        Console.Error.WriteLine("[azdo-mcp] stdin closed, exiting");
    }

    private static async Task<object> DispatchAsync(string? method, JsonElement? args, CancellationToken ct) => method switch
    {
        "initialize" => InitializeResult(),
        "tools/list" => new { tools = McpTools.All.Select(t => t.ToManifest()).ToArray() },
        "tools/call" => await CallToolAsync(args, ct),
        "ping" => new { },
        _ => throw new JsonRpcException(-32601, $"Method not found: {method}")
    };

    private static object InitializeResult() => new
    {
        protocolVersion = ProtocolVersion,
        capabilities = new { tools = new { } },
        serverInfo = new { name = ServerName, version = ServerVersion }
    };

    private static async Task<object> CallToolAsync(JsonElement? args, CancellationToken ct)
    {
        if (args is null) throw new JsonRpcException(-32602, "missing params");
        var nameEl = args.Value.TryGetProperty("name", out var n) ? n : default;
        if (nameEl.ValueKind != JsonValueKind.String) throw new JsonRpcException(-32602, "tool name required");
        var name = nameEl.GetString()!;
        var argsObj = args.Value.TryGetProperty("arguments", out var a) ? a : default;

        var tool = McpTools.All.FirstOrDefault(t => t.Name == name)
            ?? throw new JsonRpcException(-32602, $"Unknown tool: {name}");

        try
        {
            var text = await tool.Handler(argsObj, ct);
            return new
            {
                content = new[] { new { type = "text", text } },
                isError = false
            };
        }
        catch (Exception ex)
        {
            return new
            {
                content = new[] { new { type = "text", text = ex.Message } },
                isError = true
            };
        }
    }
}
