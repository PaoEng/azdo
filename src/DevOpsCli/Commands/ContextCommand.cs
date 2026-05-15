using System.CommandLine;
using DevOpsCli.Config;
using DevOpsCli.Context;

namespace DevOpsCli.Commands;

public static class ContextCommand
{
    public static Command Build()
    {
        var cmd = new Command("context", "Show Azure DevOps context detected from current directory");
        cmd.SetHandler(() =>
        {
            var detected = OrgDetector.Detect();
            if (detected is null)
            {
                Console.WriteLine("No Azure DevOps context detected (not a git repo, or remote is not Azure DevOps).");
                return;
            }

            var cfg = ConfigStore.Load();
            var hasPat = cfg.Organizations.TryGetValue(detected.Org, out var entry)
                         && !string.IsNullOrWhiteSpace(entry?.Pat);

            Console.WriteLine($"Org        : {detected.Org}");
            Console.WriteLine($"Org URL    : {detected.OrgUrl}");
            Console.WriteLine($"Project    : {detected.Project ?? "(unknown)"}");
            Console.WriteLine($"Repository : {detected.Repo ?? "(unknown)"}");
            Console.WriteLine($"Source     : {detected.Source}");
            Console.WriteLine($"Registered : {(hasPat ? "yes" : "NO — will prompt for PAT on first call")}");
            Console.WriteLine($"Config file: {ConfigStore.ConfigPath}");
        });
        return cmd;
    }
}
