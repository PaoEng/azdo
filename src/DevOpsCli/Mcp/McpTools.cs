using System.Text;
using System.Text.Json;
using DevOpsCli.AzureDevOps;
using DevOpsCli.Commands;
using DevOpsCli.Config;
using DevOpsCli.Context;

namespace DevOpsCli.Mcp;

public delegate Task<string> ToolHandler(JsonElement args, CancellationToken ct);

public sealed record ToolDef(string Name, string Description, object InputSchema, ToolHandler Handler)
{
    public object ToManifest() => new
    {
        name = Name,
        description = Description,
        inputSchema = InputSchema
    };
}

public static class McpTools
{
    private static readonly JsonSerializerOptions JsonOut = new() { WriteIndented = true };

    public static readonly ToolDef[] All =
    {
        Context(),
        WiQuery(),
        WiGet(),
        WiCreate(),
        WiUpdate(),
        WiComment(),
        RepoList(),
        PrList(),
        PrCreate(),
        BuildList(),
        BuildStatus(),
        BuildTrigger(),
    };

    private static object Str(string desc) => new { type = "string", description = desc };
    private static object Int(string desc) => new { type = "integer", description = desc };
    private static object Bool(string desc) => new { type = "boolean", description = desc };
    private static object StrArr(string desc) => new { type = "array", items = new { type = "string" }, description = desc };
    private static object IntArr(string desc) => new { type = "array", items = new { type = "integer" }, description = desc };

    private static object ObjSchema(params (string name, bool required, object schema)[] fields)
    {
        var props = new Dictionary<string, object>();
        var required = new List<string>();
        foreach (var (name, req, schema) in fields)
        {
            props[name] = schema;
            if (req) required.Add(name);
        }
        return new
        {
            type = "object",
            properties = props,
            required = required.ToArray()
        };
    }

    private static string? S(JsonElement args, string key) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static int? I(JsonElement args, string key)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(key, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var ns)) return ns;
        return null;
    }

    private static bool? B(JsonElement args, string key)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(key, out var v)) return null;
        if (v.ValueKind == JsonValueKind.True) return true;
        if (v.ValueKind == JsonValueKind.False) return false;
        if (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var bs)) return bs;
        return null;
    }

    private static string[] SA(JsonElement args, string key)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        var list = new List<string>();
        foreach (var item in v.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } s)
                list.Add(s);
        return list.ToArray();
    }

    private static int[] IA(JsonElement args, string key)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Array)
            return Array.Empty<int>();
        var list = new List<int>();
        foreach (var item in v.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var n)) list.Add(n);
            else if (item.ValueKind == JsonValueKind.String && int.TryParse(item.GetString(), out var ns)) list.Add(ns);
        }
        return list.ToArray();
    }

    private static string Format(JsonElement el) => JsonSerializer.Serialize(el, JsonOut);

    private static ToolDef Context() => new(
        "azdo_context",
        "Detect Azure DevOps org/project/repo from the current git remote",
        ObjSchema(),
        (_, _) =>
        {
            var d = OrgDetector.Detect();
            if (d is null) return Task.FromResult("No Azure DevOps context detected.");
            var cfg = ConfigStore.Load();
            var registered = cfg.Organizations.ContainsKey(d.Org);
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                org = d.Org,
                orgUrl = d.OrgUrl,
                project = d.Project,
                repo = d.Repo,
                source = d.Source,
                registered
            }, JsonOut));
        });

    private static ToolDef WiQuery() => new(
        "azdo_wi_query",
        "Run a WIQL query against work items",
        ObjSchema(
            ("wiql", true, Str("WIQL query")),
            ("org", false, Str("Override detected org")),
            ("project", false, Str("Override detected project"))),
        async (args, ct) =>
        {
            var wiql = S(args, "wiql") ?? throw new ArgumentException("wiql is required");
            using var s = ClientFactory.OpenSession(S(args, "org"), S(args, "project"));
            RequireProject(s);
            var body = AzureDevOpsClient.JsonBody(new { query = wiql });
            var result = await s.Client.PostAsync($"{s.Project}/_apis/wit/wiql", body, ct);
            return Format(result);
        });

    private static ToolDef WiGet() => new(
        "azdo_wi_get",
        "Get one or more work items by ID",
        ObjSchema(
            ("ids", true, Str("Comma-separated work item IDs")),
            ("fields", false, Str("Comma-separated fields, e.g. System.Title,System.State")),
            ("org", false, Str("Override detected org")),
            ("project", false, Str("Override detected project"))),
        async (args, ct) =>
        {
            var ids = S(args, "ids") ?? throw new ArgumentException("ids is required");
            var fields = S(args, "fields");
            using var s = ClientFactory.OpenSession(S(args, "org"), S(args, "project"));
            var path = $"_apis/wit/workitems?ids={Uri.EscapeDataString(ids)}";
            if (!string.IsNullOrWhiteSpace(fields))
                path += $"&fields={Uri.EscapeDataString(fields)}";
            var result = await s.Client.GetAsync(path, ct);
            return Format(result);
        });

    private static ToolDef WiCreate() => new(
        "azdo_wi_create",
        "Create a new work item (Task, Bug, User Story, Feature, Epic)",
        ObjSchema(
            ("type", true, Str("Work item type")),
            ("title", true, Str("Title")),
            ("description", false, Str("Description")),
            ("assignedTo", false, Str("Assignee email")),
            ("parent", false, Int("Parent work item ID")),
            ("iteration", false, Str("Iteration path")),
            ("org", false, Str("Override detected org")),
            ("project", false, Str("Override detected project"))),
        async (args, ct) =>
        {
            var type = S(args, "type") ?? throw new ArgumentException("type is required");
            var title = S(args, "title") ?? throw new ArgumentException("title is required");
            using var s = ClientFactory.OpenSession(S(args, "org"), S(args, "project"));
            RequireProject(s);

            var ops = new List<object>
            {
                new { op = "add", path = "/fields/System.Title", value = title }
            };
            if (S(args, "description") is { } desc)
                ops.Add(new { op = "add", path = "/fields/System.Description", value = desc });
            if (S(args, "assignedTo") is { } assignee)
                ops.Add(new { op = "add", path = "/fields/System.AssignedTo", value = assignee });
            if (S(args, "iteration") is { } iter)
                ops.Add(new { op = "add", path = "/fields/System.IterationPath", value = iter });
            if (I(args, "parent") is { } pid)
                ops.Add(new
                {
                    op = "add",
                    path = "/relations/-",
                    value = new
                    {
                        rel = "System.LinkTypes.Hierarchy-Reverse",
                        url = $"{s.Client.OrgUrl}/_apis/wit/workItems/{pid}"
                    }
                });

            var body = AzureDevOpsClient.JsonPatchBody(ops);
            var result = await s.Client.PostAsync(
                $"{s.Project}/_apis/wit/workitems/${Uri.EscapeDataString(type)}", body, ct);
            return Format(result);
        });

    private static ToolDef WiUpdate() => new(
        "azdo_wi_update",
        "Update an existing work item",
        ObjSchema(
            ("id", true, Int("Work item ID")),
            ("state", false, Str("New state")),
            ("title", false, Str("New title")),
            ("assignedTo", false, Str("Assignee email")),
            ("parent", false, Int("New parent work item ID")),
            ("iteration", false, Str("Iteration path")),
            ("org", false, Str("Override detected org")),
            ("project", false, Str("Override detected project"))),
        async (args, ct) =>
        {
            var id = I(args, "id") ?? throw new ArgumentException("id is required");
            using var s = ClientFactory.OpenSession(S(args, "org"), S(args, "project"));
            var ops = new List<object>();
            if (S(args, "state") is { } st) ops.Add(new { op = "add", path = "/fields/System.State", value = st });
            if (S(args, "title") is { } t) ops.Add(new { op = "add", path = "/fields/System.Title", value = t });
            if (S(args, "assignedTo") is { } a) ops.Add(new { op = "add", path = "/fields/System.AssignedTo", value = a });
            if (S(args, "iteration") is { } iter) ops.Add(new { op = "add", path = "/fields/System.IterationPath", value = iter });
            if (I(args, "parent") is { } pid)
                ops.Add(new
                {
                    op = "add",
                    path = "/relations/-",
                    value = new
                    {
                        rel = "System.LinkTypes.Hierarchy-Reverse",
                        url = $"{s.Client.OrgUrl}/_apis/wit/workItems/{pid}"
                    }
                });
            if (ops.Count == 0) throw new ArgumentException("No fields to update.");
            var body = AzureDevOpsClient.JsonPatchBody(ops);
            var result = await s.Client.PatchAsync($"_apis/wit/workitems/{id}", body, ct);
            return Format(result);
        });

    private static ToolDef WiComment() => new(
        "azdo_wi_comment",
        "Add a comment to a work item",
        ObjSchema(
            ("id", true, Int("Work item ID")),
            ("text", true, Str("Comment text")),
            ("org", false, Str("Override detected org")),
            ("project", false, Str("Override detected project"))),
        async (args, ct) =>
        {
            var id = I(args, "id") ?? throw new ArgumentException("id is required");
            var text = S(args, "text") ?? throw new ArgumentException("text is required");
            using var s = ClientFactory.OpenSession(S(args, "org"), S(args, "project"));
            RequireProject(s);
            var body = AzureDevOpsClient.JsonBody(new { text });
            var result = await s.Client.PostAsync($"{s.Project}/_apis/wit/workItems/{id}/comments", body, ct);
            return Format(result);
        });

    private static ToolDef RepoList() => new(
        "azdo_repo_list",
        "List repositories in the project",
        ObjSchema(
            ("org", false, Str("Override detected org")),
            ("project", false, Str("Override detected project"))),
        async (args, ct) =>
        {
            using var s = ClientFactory.OpenSession(S(args, "org"), S(args, "project"));
            var path = string.IsNullOrEmpty(s.Project)
                ? "_apis/git/repositories"
                : $"{s.Project}/_apis/git/repositories";
            var result = await s.Client.GetAsync(path, ct);
            return Format(result);
        });

    private static ToolDef PrList() => new(
        "azdo_pr_list",
        "List pull requests (org/project, optionally scoped to a repo)",
        ObjSchema(
            ("repo", false, Str("Repository ID or name")),
            ("status", false, Str("active | abandoned | completed | all")),
            ("creator", false, Str("Creator descriptor or email")),
            ("org", false, Str("Override detected org")),
            ("project", false, Str("Override detected project"))),
        async (args, ct) =>
        {
            using var s = ClientFactory.OpenSession(S(args, "org"), S(args, "project"));
            var qs = new StringBuilder();
            void Add(string k, string? v)
            {
                if (string.IsNullOrWhiteSpace(v)) return;
                qs.Append(qs.Length == 0 ? '?' : '&');
                qs.Append(k).Append('=').Append(Uri.EscapeDataString(v));
            }
            Add("searchCriteria.status", S(args, "status"));
            Add("searchCriteria.creatorId", S(args, "creator"));

            var repo = S(args, "repo");
            string path;
            if (!string.IsNullOrWhiteSpace(repo))
            {
                var scope = string.IsNullOrEmpty(s.Project) ? "" : s.Project + "/";
                path = $"{scope}_apis/git/repositories/{Uri.EscapeDataString(repo)}/pullrequests{qs}";
            }
            else if (!string.IsNullOrEmpty(s.Project))
                path = $"{s.Project}/_apis/git/pullrequests{qs}";
            else
                path = $"_apis/git/pullrequests{qs}";

            var result = await s.Client.GetAsync(path, ct);
            return Format(result);
        });

    private static ToolDef PrCreate() => new(
        "azdo_pr_create",
        "Create a pull request. Defaults: repo = detected from git remote, source = current git branch, target = main.",
        ObjSchema(
            ("title", true, Str("Pull request title")),
            ("repo", false, Str("Repository ID or name (default: detected)")),
            ("source", false, Str("Source branch (default: current git branch)")),
            ("target", false, Str("Target branch (default: main)")),
            ("description", false, Str("PR description")),
            ("draft", false, Bool("Create as draft (default false)")),
            ("workItems", false, IntArr("Work item IDs to link")),
            ("reviewers", false, StrArr("Reviewer identity GUIDs (email not resolved server-side)")),
            ("org", false, Str("Override detected org")),
            ("project", false, Str("Override detected project"))),
        async (args, ct) =>
        {
            var title = S(args, "title") ?? throw new ArgumentException("title is required");
            using var s = ClientFactory.OpenSession(S(args, "org"), S(args, "project"));

            var repo = S(args, "repo") ?? s.Detected?.Repo
                ?? throw new InvalidOperationException(
                    "Repository not specified and not detected from git remote. Pass 'repo'.");
            var source = S(args, "source") ?? OrgDetector.CurrentBranch()
                ?? throw new InvalidOperationException(
                    "Source branch not specified and current git branch not detected. Pass 'source'.");
            var target = S(args, "target") ?? "main";

            var body = PullRequestCommand.BuildCreateBody(
                source, target, title,
                S(args, "description"),
                B(args, "draft") ?? false,
                IA(args, "workItems"),
                SA(args, "reviewers"));

            var scope = string.IsNullOrEmpty(s.Project) ? "" : s.Project + "/";
            var path = $"{scope}_apis/git/repositories/{Uri.EscapeDataString(repo)}/pullrequests";
            var result = await s.Client.PostAsync(path, AzureDevOpsClient.JsonBody(body), ct);
            return Format(result);
        });

    private static ToolDef BuildList() => new(
        "azdo_build_list",
        "List recent builds",
        ObjSchema(
            ("top", false, Int("Max number of results")),
            ("definition", false, Int("Pipeline definition ID filter")),
            ("org", false, Str("Override detected org")),
            ("project", false, Str("Override detected project"))),
        async (args, ct) =>
        {
            using var s = ClientFactory.OpenSession(S(args, "org"), S(args, "project"));
            RequireProject(s);
            var qs = new List<string>();
            if (I(args, "top") is { } t) qs.Add($"$top={t}");
            if (I(args, "definition") is { } d) qs.Add($"definitions={d}");
            var query = qs.Count > 0 ? "?" + string.Join('&', qs) : "";
            var result = await s.Client.GetAsync($"{s.Project}/_apis/build/builds{query}", ct);
            return Format(result);
        });

    private static ToolDef BuildStatus() => new(
        "azdo_build_status",
        "Get build status and timeline",
        ObjSchema(
            ("id", true, Int("Build ID")),
            ("org", false, Str("Override detected org")),
            ("project", false, Str("Override detected project"))),
        async (args, ct) =>
        {
            var id = I(args, "id") ?? throw new ArgumentException("id is required");
            using var s = ClientFactory.OpenSession(S(args, "org"), S(args, "project"));
            RequireProject(s);
            var build = await s.Client.GetAsync($"{s.Project}/_apis/build/builds/{id}", ct);
            var timeline = await s.Client.GetAsync($"{s.Project}/_apis/build/builds/{id}/timeline", ct);
            return JsonSerializer.Serialize(new { build, timeline }, JsonOut);
        });

    private static ToolDef BuildTrigger() => new(
        "azdo_build_trigger",
        "Trigger a pipeline run",
        ObjSchema(
            ("definition", true, Int("Pipeline definition ID")),
            ("branch", false, Str("Source branch (e.g. refs/heads/main)")),
            ("org", false, Str("Override detected org")),
            ("project", false, Str("Override detected project"))),
        async (args, ct) =>
        {
            var def = I(args, "definition") ?? throw new ArgumentException("definition is required");
            var branch = S(args, "branch");
            using var s = ClientFactory.OpenSession(S(args, "org"), S(args, "project"));
            RequireProject(s);
            object payload = string.IsNullOrWhiteSpace(branch)
                ? new { definition = new { id = def } }
                : new { definition = new { id = def }, sourceBranch = branch };
            var body = AzureDevOpsClient.JsonBody(payload);
            var result = await s.Client.PostAsync($"{s.Project}/_apis/build/builds", body, ct);
            return Format(result);
        });

    private static void RequireProject(Session s)
    {
        if (string.IsNullOrWhiteSpace(s.Project))
            throw new InvalidOperationException(
                "Project is required for this operation. Pass 'project' in tool args, or set a defaultProject on the org via 'azdo config add'.");
    }
}
