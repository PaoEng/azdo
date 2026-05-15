using System.CommandLine;
using System.Text.Json;
using DevOpsCli.AzureDevOps;

namespace DevOpsCli.Commands;

public static class WorkItemCommand
{
    public static Command Build()
    {
        var root = new Command("wi", "Work item operations");
        root.AddAlias("workitem");

        root.AddCommand(BuildQuery());
        root.AddCommand(BuildGet());
        root.AddCommand(BuildCreate());
        root.AddCommand(BuildUpdate());
        root.AddCommand(BuildComment());
        return root;
    }

    private static readonly Option<string?> OrgOpt = new("--org", "Override detected org");
    private static readonly Option<string?> ProjectOpt = new("--project", "Override detected project");

    private static Command BuildQuery()
    {
        var wiqlOpt = new Option<string>("--wiql", "WIQL query") { IsRequired = true };
        var cmd = new Command("query", "Run a WIQL query") { wiqlOpt, OrgOpt, ProjectOpt };
        cmd.SetHandler(async (string wiql, string? org, string? project) =>
        {
            using var session = ClientFactory.OpenSession(org, project);
            RequireProject(session);
            var body = AzureDevOpsClient.JsonBody(new { query = wiql });
            var result = await session.Client.PostAsync($"{session.Project}/_apis/wit/wiql", body);
            PrintJson(result);
        }, wiqlOpt, OrgOpt, ProjectOpt);
        return cmd;
    }

    private static Command BuildGet()
    {
        var idsOpt = new Option<string>("--ids", "Comma-separated work item IDs") { IsRequired = true };
        var fieldsOpt = new Option<string?>("--fields", "Comma-separated fields (e.g. System.Title,System.State)");
        var cmd = new Command("get", "Get one or more work items by ID") { idsOpt, fieldsOpt, OrgOpt, ProjectOpt };
        cmd.SetHandler(async (string ids, string? fields, string? org, string? project) =>
        {
            using var session = ClientFactory.OpenSession(org, project);
            var path = $"_apis/wit/workitems?ids={Uri.EscapeDataString(ids)}";
            if (!string.IsNullOrWhiteSpace(fields))
                path += $"&fields={Uri.EscapeDataString(fields)}";
            var result = await session.Client.GetAsync(path);
            PrintJson(result);
        }, idsOpt, fieldsOpt, OrgOpt, ProjectOpt);
        return cmd;
    }

    private static Command BuildCreate()
    {
        var typeOpt = new Option<string>("--type", "Work item type (Task, Bug, User Story, Feature, Epic)") { IsRequired = true };
        var titleOpt = new Option<string>("--title", "Title") { IsRequired = true };
        var descOpt = new Option<string?>("--description", "Description");
        var assignOpt = new Option<string?>("--assigned-to", "Assignee email");
        var parentOpt = new Option<int?>("--parent", "Parent work item ID");
        var iterOpt = new Option<string?>("--iteration", "Iteration path");
        var cmd = new Command("create", "Create a new work item")
        {
            typeOpt, titleOpt, descOpt, assignOpt, parentOpt, iterOpt, OrgOpt, ProjectOpt
        };
        cmd.SetHandler(async (System.CommandLine.Invocation.InvocationContext ctx) =>
        {
            var type = ctx.ParseResult.GetValueForOption(typeOpt)!;
            var title = ctx.ParseResult.GetValueForOption(titleOpt)!;
            var description = ctx.ParseResult.GetValueForOption(descOpt);
            var assignedTo = ctx.ParseResult.GetValueForOption(assignOpt);
            var parent = ctx.ParseResult.GetValueForOption(parentOpt);
            var iteration = ctx.ParseResult.GetValueForOption(iterOpt);
            var org = ctx.ParseResult.GetValueForOption(OrgOpt);
            var project = ctx.ParseResult.GetValueForOption(ProjectOpt);

            using var session = ClientFactory.OpenSession(org, project);
            RequireProject(session);

            var ops = new List<object>
            {
                new { op = "add", path = "/fields/System.Title", value = title }
            };
            if (!string.IsNullOrWhiteSpace(description))
                ops.Add(new { op = "add", path = "/fields/System.Description", value = description });
            if (!string.IsNullOrWhiteSpace(assignedTo))
                ops.Add(new { op = "add", path = "/fields/System.AssignedTo", value = assignedTo });
            if (!string.IsNullOrWhiteSpace(iteration))
                ops.Add(new { op = "add", path = "/fields/System.IterationPath", value = iteration });
            if (parent.HasValue)
                ops.Add(new
                {
                    op = "add",
                    path = "/relations/-",
                    value = new
                    {
                        rel = "System.LinkTypes.Hierarchy-Reverse",
                        url = $"{session.Client.OrgUrl}/_apis/wit/workItems/{parent.Value}"
                    }
                });

            var body = AzureDevOpsClient.JsonPatchBody(ops);
            var encodedType = Uri.EscapeDataString(type);
            var result = await session.Client.PostAsync($"{session.Project}/_apis/wit/workitems/${encodedType}", body);
            PrintJson(result);
        });
        return cmd;
    }

    private static Command BuildUpdate()
    {
        var idOpt = new Option<int>("--id", "Work item ID") { IsRequired = true };
        var stateOpt = new Option<string?>("--state", "New state");
        var titleOpt = new Option<string?>("--title", "New title");
        var assignOpt = new Option<string?>("--assigned-to", "Assignee email");
        var parentOpt = new Option<int?>("--parent", "New parent work item ID");
        var iterOpt = new Option<string?>("--iteration", "Iteration path");
        var cmd = new Command("update", "Update an existing work item")
        {
            idOpt, stateOpt, titleOpt, assignOpt, parentOpt, iterOpt, OrgOpt, ProjectOpt
        };
        cmd.SetHandler(async (System.CommandLine.Invocation.InvocationContext ctx) =>
        {
            var id = ctx.ParseResult.GetValueForOption(idOpt);
            var state = ctx.ParseResult.GetValueForOption(stateOpt);
            var title = ctx.ParseResult.GetValueForOption(titleOpt);
            var assignedTo = ctx.ParseResult.GetValueForOption(assignOpt);
            var parent = ctx.ParseResult.GetValueForOption(parentOpt);
            var iteration = ctx.ParseResult.GetValueForOption(iterOpt);
            var org = ctx.ParseResult.GetValueForOption(OrgOpt);
            var project = ctx.ParseResult.GetValueForOption(ProjectOpt);

            using var session = ClientFactory.OpenSession(org, project);

            var ops = new List<object>();
            if (!string.IsNullOrWhiteSpace(state))
                ops.Add(new { op = "add", path = "/fields/System.State", value = state });
            if (!string.IsNullOrWhiteSpace(title))
                ops.Add(new { op = "add", path = "/fields/System.Title", value = title });
            if (!string.IsNullOrWhiteSpace(assignedTo))
                ops.Add(new { op = "add", path = "/fields/System.AssignedTo", value = assignedTo });
            if (!string.IsNullOrWhiteSpace(iteration))
                ops.Add(new { op = "add", path = "/fields/System.IterationPath", value = iteration });
            if (parent.HasValue)
                ops.Add(new
                {
                    op = "add",
                    path = "/relations/-",
                    value = new
                    {
                        rel = "System.LinkTypes.Hierarchy-Reverse",
                        url = $"{session.Client.OrgUrl}/_apis/wit/workItems/{parent.Value}"
                    }
                });

            if (ops.Count == 0)
            {
                Console.Error.WriteLine("No fields to update.");
                return;
            }

            var body = AzureDevOpsClient.JsonPatchBody(ops);
            var result = await session.Client.PatchAsync($"_apis/wit/workitems/{id}", body);
            PrintJson(result);
        });
        return cmd;
    }

    private static Command BuildComment()
    {
        var idOpt = new Option<int>("--id", "Work item ID") { IsRequired = true };
        var textOpt = new Option<string>("--text", "Comment text") { IsRequired = true };
        var cmd = new Command("comment", "Add a comment to a work item") { idOpt, textOpt, OrgOpt, ProjectOpt };
        cmd.SetHandler(async (int id, string text, string? org, string? project) =>
        {
            using var session = ClientFactory.OpenSession(org, project);
            RequireProject(session);
            var body = AzureDevOpsClient.JsonBody(new { text });
            var result = await session.Client.PostAsync(
                $"{session.Project}/_apis/wit/workItems/{id}/comments", body);
            PrintJson(result);
        }, idOpt, textOpt, OrgOpt, ProjectOpt);
        return cmd;
    }

    private static void RequireProject(Session s)
    {
        if (string.IsNullOrWhiteSpace(s.Project))
            throw new InvalidOperationException(
                "Project is required for this operation. Pass --project or set a defaultProject on the org.");
    }

    private static void PrintJson(JsonElement el)
    {
        Console.WriteLine(JsonSerializer.Serialize(el, new JsonSerializerOptions { WriteIndented = true }));
    }
}
