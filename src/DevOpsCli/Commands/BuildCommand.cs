using System.CommandLine;
using System.Text.Json;
using DevOpsCli.AzureDevOps;

namespace DevOpsCli.Commands;

public static class BuildCommand
{
    private static readonly Option<string?> OrgOpt = new("--org", "Override detected org");
    private static readonly Option<string?> ProjectOpt = new("--project", "Override detected project");

    public static Command Build()
    {
        var root = new Command("build", "Build and pipeline operations");
        root.AddCommand(BuildList());
        root.AddCommand(BuildStatus());
        root.AddCommand(BuildTrigger());
        return root;
    }

    private static Command BuildList()
    {
        var topOpt = new Option<int?>("--top", "Limit");
        var defOpt = new Option<int?>("--definition", "Definition ID filter");
        var cmd = new Command("list", "List recent builds") { topOpt, defOpt, OrgOpt, ProjectOpt };
        cmd.SetHandler(async (int? top, int? definition, string? org, string? project) =>
        {
            using var session = ClientFactory.OpenSession(org, project);
            RequireProject(session);
            var qs = new List<string>();
            if (top.HasValue) qs.Add($"$top={top}");
            if (definition.HasValue) qs.Add($"definitions={definition}");
            var query = qs.Count > 0 ? "?" + string.Join('&', qs) : "";
            var result = await session.Client.GetAsync($"{session.Project}/_apis/build/builds{query}");
            PrintJson(result);
        }, topOpt, defOpt, OrgOpt, ProjectOpt);
        return cmd;
    }

    private static Command BuildStatus()
    {
        var idOpt = new Option<int>("--id", "Build ID") { IsRequired = true };
        var cmd = new Command("status", "Get build status and timeline") { idOpt, OrgOpt, ProjectOpt };
        cmd.SetHandler(async (int id, string? org, string? project) =>
        {
            using var session = ClientFactory.OpenSession(org, project);
            RequireProject(session);
            var build = await session.Client.GetAsync($"{session.Project}/_apis/build/builds/{id}");
            var timeline = await session.Client.GetAsync($"{session.Project}/_apis/build/builds/{id}/timeline");
            Console.WriteLine("=== Build ===");
            PrintJson(build);
            Console.WriteLine("=== Timeline ===");
            PrintJson(timeline);
        }, idOpt, OrgOpt, ProjectOpt);
        return cmd;
    }

    private static Command BuildTrigger()
    {
        var defOpt = new Option<int>("--definition", "Pipeline definition ID") { IsRequired = true };
        var branchOpt = new Option<string?>("--branch", "Source branch (e.g. refs/heads/main)");
        var cmd = new Command("trigger", "Trigger a pipeline run") { defOpt, branchOpt, OrgOpt, ProjectOpt };
        cmd.SetHandler(async (int definition, string? branch, string? org, string? project) =>
        {
            using var session = ClientFactory.OpenSession(org, project);
            RequireProject(session);
            object payload = string.IsNullOrWhiteSpace(branch)
                ? new { definition = new { id = definition } }
                : new { definition = new { id = definition }, sourceBranch = branch };
            var body = AzureDevOpsClient.JsonBody(payload);
            var result = await session.Client.PostAsync($"{session.Project}/_apis/build/builds", body);
            PrintJson(result);
        }, defOpt, branchOpt, OrgOpt, ProjectOpt);
        return cmd;
    }

    private static void RequireProject(Session s)
    {
        if (string.IsNullOrWhiteSpace(s.Project))
            throw new InvalidOperationException(
                "Project is required for this operation. Pass --project or set defaultProject on the org.");
    }

    private static void PrintJson(JsonElement el) =>
        Console.WriteLine(JsonSerializer.Serialize(el, new JsonSerializerOptions { WriteIndented = true }));
}
