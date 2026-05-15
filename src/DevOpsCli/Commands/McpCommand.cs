using System.CommandLine;
using DevOpsCli.Mcp;

namespace DevOpsCli.Commands;

public static class McpCommand
{
    public static Command Build()
    {
        var root = new Command("mcp", "Model Context Protocol server mode (for GitHub Copilot CLI, Claude, etc.)");

        var serve = new Command("serve", "Run as MCP server over stdio (JSON-RPC 2.0)");
        serve.SetHandler(async () =>
        {
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
            await new McpServer().RunAsync(cts.Token);
        });

        var listTools = new Command("list-tools", "Print the tool manifest (debug / inspection)");
        listTools.SetHandler(() =>
        {
            var manifest = McpTools.All.Select(t => t.ToManifest()).ToArray();
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
                new { tools = manifest },
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        });

        root.AddCommand(serve);
        root.AddCommand(listTools);
        return root;
    }
}
