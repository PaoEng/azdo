using System.CommandLine;
using System.Text;
using System.Text.Json;
using DevOpsCli.AzureDevOps;
using DevOpsCli.Context;

namespace DevOpsCli.Commands;

public static class PullRequestCommand
{
    public static Command Build()
    {
        var orgOpt = new Option<string?>("--org", "Override detected org");
        var projectOpt = new Option<string?>("--project", "Override detected project");

        var root = new Command("pr", "Pull request operations");
        root.AddCommand(BuildList(orgOpt, projectOpt));
        root.AddCommand(BuildCreate(orgOpt, projectOpt));
        return root;
    }

    private static Command BuildList(Option<string?> orgOpt, Option<string?> projectOpt)
    {
        var repoOpt = new Option<string?>("--repo", "Repository ID or name (defaults to all)");
        var statusOpt = new Option<string?>("--status", "active | abandoned | completed | all");
        var creatorOpt = new Option<string?>("--creator", "Creator descriptor or email");

        var list = new Command("list", "List pull requests")
        {
            orgOpt, projectOpt, repoOpt, statusOpt, creatorOpt
        };
        list.SetHandler(async (string? org, string? project, string? repo, string? status, string? creator) =>
        {
            using var session = ClientFactory.OpenSession(org, project);

            var qs = new StringBuilder();
            void Add(string key, string? val)
            {
                if (string.IsNullOrWhiteSpace(val)) return;
                qs.Append(qs.Length == 0 ? '?' : '&');
                qs.Append(key).Append('=').Append(Uri.EscapeDataString(val));
            }
            Add("searchCriteria.status", status);
            Add("searchCriteria.creatorId", creator);

            string path;
            if (!string.IsNullOrWhiteSpace(repo))
            {
                var scope = string.IsNullOrWhiteSpace(session.Project) ? "" : session.Project + "/";
                path = $"{scope}_apis/git/repositories/{Uri.EscapeDataString(repo)}/pullrequests{qs}";
            }
            else if (!string.IsNullOrWhiteSpace(session.Project))
            {
                path = $"{session.Project}/_apis/git/pullrequests{qs}";
            }
            else
            {
                path = $"_apis/git/pullrequests{qs}";
            }

            var result = await session.Client.GetAsync(path);
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }, orgOpt, projectOpt, repoOpt, statusOpt, creatorOpt);
        return list;
    }

    private static Command BuildCreate(Option<string?> orgOpt, Option<string?> projectOpt)
    {
        var repoOpt = new Option<string?>("--repo", "Repository ID or name (default: detected from git remote)");
        var sourceOpt = new Option<string?>("--source", "Source branch (default: current git branch)");
        var targetOpt = new Option<string>("--target", () => "main", "Target branch (default: main)");
        var titleOpt = new Option<string>("--title", "PR title") { IsRequired = true };
        var descOpt = new Option<string?>("--description", "PR description");
        var draftOpt = new Option<bool>("--draft", () => false, "Create as draft");
        var workItemOpt = new Option<int[]>("--work-item", "Work item IDs to link (repeatable)") { AllowMultipleArgumentsPerToken = true };
        var reviewerOpt = new Option<string[]>("--reviewer", "Reviewer identity GUID (repeatable)") { AllowMultipleArgumentsPerToken = true };

        var create = new Command("create", "Create a pull request")
        {
            orgOpt, projectOpt, repoOpt, sourceOpt, targetOpt,
            titleOpt, descOpt, draftOpt, workItemOpt, reviewerOpt
        };

        create.SetHandler(async (System.CommandLine.Invocation.InvocationContext ctx) =>
        {
            var org = ctx.ParseResult.GetValueForOption(orgOpt);
            var project = ctx.ParseResult.GetValueForOption(projectOpt);
            var repoArg = ctx.ParseResult.GetValueForOption(repoOpt);
            var source = ctx.ParseResult.GetValueForOption(sourceOpt);
            var target = ctx.ParseResult.GetValueForOption(targetOpt) ?? "main";
            var title = ctx.ParseResult.GetValueForOption(titleOpt)!;
            var description = ctx.ParseResult.GetValueForOption(descOpt);
            var draft = ctx.ParseResult.GetValueForOption(draftOpt);
            var workItems = ctx.ParseResult.GetValueForOption(workItemOpt) ?? Array.Empty<int>();
            var reviewers = ctx.ParseResult.GetValueForOption(reviewerOpt) ?? Array.Empty<string>();

            using var session = ClientFactory.OpenSession(org, project);

            var repo = repoArg ?? session.Detected?.Repo
                ?? throw new InvalidOperationException(
                    "Repository not specified and not detected from git remote. Use --repo.");
            source ??= OrgDetector.CurrentBranch()
                ?? throw new InvalidOperationException(
                    "Source branch not specified and current git branch not detected. Use --source.");

            var body = BuildCreateBody(source, target, title, description, draft, workItems, reviewers);
            var scope = string.IsNullOrWhiteSpace(session.Project) ? "" : session.Project + "/";
            var path = $"{scope}_apis/git/repositories/{Uri.EscapeDataString(repo)}/pullrequests";

            var result = await session.Client.PostAsync(path, AzureDevOpsClient.JsonBody(body));
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        });
        return create;
    }

    internal static object BuildCreateBody(
        string source, string target, string title, string? description,
        bool draft, int[] workItems, string[] reviewers)
    {
        var dict = new Dictionary<string, object>
        {
            ["sourceRefName"] = NormalizeRef(source),
            ["targetRefName"] = NormalizeRef(target),
            ["title"] = title,
            ["isDraft"] = draft,
        };
        if (!string.IsNullOrWhiteSpace(description))
            dict["description"] = description;
        if (workItems is { Length: > 0 })
            dict["workItemRefs"] = workItems.Select(id => new { id = id.ToString() }).ToArray();
        if (reviewers is { Length: > 0 })
            dict["reviewers"] = reviewers.Select(id => new { id }).ToArray();
        return dict;
    }

    private static string NormalizeRef(string branch) =>
        branch.StartsWith("refs/", StringComparison.OrdinalIgnoreCase) ? branch : $"refs/heads/{branch}";
}
