using System.CommandLine;
using DevOpsCli.Commands;

var root = new RootCommand("Azure DevOps CLI — central config, git-remote org detection")
{
    ContextCommand.Build(),
    ConfigCommand.Build(),
    WorkItemCommand.Build(),
    RepoCommand.Build(),
    PullRequestCommand.Build(),
    BuildCommand.Build(),
    McpCommand.Build()
};

try
{
    return await root.InvokeAsync(args);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}
