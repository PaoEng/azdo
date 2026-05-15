using System.Text.Json;
using DevOpsCli.Mcp;

namespace DevOpsCli.Tests;

public class McpToolsTests
{
    private static readonly string[] ExpectedToolNames =
    [
        "azdo_context",
        "azdo_wi_query",
        "azdo_wi_get",
        "azdo_wi_create",
        "azdo_wi_update",
        "azdo_wi_comment",
        "azdo_repo_list",
        "azdo_pr_list",
        "azdo_build_list",
        "azdo_build_status",
        "azdo_build_trigger",
    ];

    [Fact]
    public void All_Contains11Tools()
    {
        Assert.Equal(11, McpTools.All.Length);
    }

    [Fact]
    public void All_ContainsAllExpectedNames()
    {
        var names = McpTools.All.Select(t => t.Name).ToHashSet();
        foreach (var expected in ExpectedToolNames)
            Assert.Contains(expected, names);
    }

    [Fact]
    public void All_NoNullOrEmptyName()
    {
        foreach (var tool in McpTools.All)
            Assert.False(string.IsNullOrWhiteSpace(tool.Name), $"Tool at index {Array.IndexOf(McpTools.All, tool)} has empty name");
    }

    [Fact]
    public void All_NoNullOrEmptyDescription()
    {
        foreach (var tool in McpTools.All)
            Assert.False(string.IsNullOrWhiteSpace(tool.Description), $"Tool '{tool.Name}' has empty description");
    }

    [Fact]
    public void All_EachSchema_HasTypeObject()
    {
        foreach (var tool in McpTools.All)
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(tool.InputSchema));
            var type = doc.RootElement.GetProperty("type").GetString();
            Assert.True(type == "object", $"Tool '{tool.Name}' inputSchema.type should be 'object', got '{type}'");
        }
    }

    [Fact]
    public void All_EachSchema_RequiredFieldsExistInProperties()
    {
        foreach (var tool in McpTools.All)
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(tool.InputSchema));
            var root = doc.RootElement;

            if (!root.TryGetProperty("required", out var requiredEl)) continue;
            if (!root.TryGetProperty("properties", out var propsEl)) continue;

            foreach (var req in requiredEl.EnumerateArray())
            {
                var fieldName = req.GetString()!;
                Assert.True(propsEl.TryGetProperty(fieldName, out _),
                    $"Tool '{tool.Name}': required field '{fieldName}' not in properties");
            }
        }
    }

    [Fact]
    public void All_EachTool_HasNonNullHandler()
    {
        foreach (var tool in McpTools.All)
            Assert.NotNull(tool.Handler);
    }

    [Fact]
    public void All_NamesAreUnique()
    {
        var names = McpTools.All.Select(t => t.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }
}
