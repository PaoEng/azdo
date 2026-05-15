using System.CommandLine;
using System.Text.Json;
using DevOpsCli.AzureDevOps;

namespace DevOpsCli.Commands;

public static class RepoCommand
{
    public static Command Build()
    {
        var orgOpt = new Option<string?>("--org", "Override detected org");
        var projectOpt = new Option<string?>("--project", "Override detected project");

        var root = new Command("repo", "Repository operations");

        var list = new Command("list", "List repositories in the current project") { orgOpt, projectOpt };
        list.SetHandler(async (string? org, string? project) =>
        {
            using var session = ClientFactory.OpenSession(org, project);
            var path = string.IsNullOrWhiteSpace(session.Project)
                ? "_apis/git/repositories"
                : $"{session.Project}/_apis/git/repositories";
            var result = await session.Client.GetAsync(path);
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }, orgOpt, projectOpt);
        root.AddCommand(list);
        return root;
    }
}
