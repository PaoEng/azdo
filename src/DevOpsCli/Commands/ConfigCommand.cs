using System.CommandLine;
using DevOpsCli.Config;

namespace DevOpsCli.Commands;

public static class ConfigCommand
{
    public static Command Build()
    {
        var root = new Command("config", "Manage central CLI configuration");

        root.AddCommand(BuildList());
        root.AddCommand(BuildAdd());
        root.AddCommand(BuildRemove());
        root.AddCommand(BuildPath());
        return root;
    }

    private static Command BuildList()
    {
        var cmd = new Command("list", "List configured organizations");
        cmd.SetHandler(() =>
        {
            var cfg = ConfigStore.Load();
            if (cfg.Organizations.Count == 0)
            {
                Console.WriteLine("No organizations configured.");
                return;
            }

            Console.WriteLine($"Config: {ConfigStore.ConfigPath}");
            Console.WriteLine();
            foreach (var (name, entry) in cfg.Organizations)
            {
                var maskedPat = string.IsNullOrEmpty(entry.Pat)
                    ? "(none)"
                    : $"****{entry.Pat[^4..]}";
                Console.WriteLine($"  {name}");
                Console.WriteLine($"    url        : {entry.OrganizationUrl}");
                Console.WriteLine($"    project    : {entry.DefaultProject ?? "(none)"}");
                Console.WriteLine($"    pat        : {maskedPat}");
                Console.WriteLine($"    updated    : {entry.LastUpdated:u}");
            }
        });
        return cmd;
    }

    private static Command BuildAdd()
    {
        var orgOpt = new Option<string>("--org", "Organization name (e.g. riversync)") { IsRequired = true };
        var patOpt = new Option<string?>("--pat", "Personal Access Token");
        var urlOpt = new Option<string?>("--url", "Override organization URL (defaults to https://dev.azure.com/{org})");
        var projOpt = new Option<string?>("--project", "Default project");
        var descOpt = new Option<string?>("--description", "Free-text description");

        var cmd = new Command("add", "Add or update an organization");
        cmd.AddOption(orgOpt);
        cmd.AddOption(patOpt);
        cmd.AddOption(urlOpt);
        cmd.AddOption(projOpt);
        cmd.AddOption(descOpt);

        cmd.SetHandler((string org, string? pat, string? url, string? project, string? description) =>
        {
            var cfg = ConfigStore.Load();
            var entry = cfg.Organizations.TryGetValue(org, out var existing) ? existing : new OrgEntry();

            entry.OrganizationUrl = !string.IsNullOrWhiteSpace(url) ? url! : $"https://dev.azure.com/{org}";

            if (string.IsNullOrWhiteSpace(pat))
            {
                Console.Write("Enter PAT (input hidden): ");
                pat = ReadSecret();
            }

            if (!string.IsNullOrWhiteSpace(pat))
                entry.Pat = pat!;

            if (!string.IsNullOrWhiteSpace(project)) entry.DefaultProject = project;
            if (!string.IsNullOrWhiteSpace(description)) entry.Description = description;
            entry.LastUpdated = DateTime.UtcNow;

            cfg.Organizations[org] = entry;
            ConfigStore.Save(cfg);
            Console.WriteLine($"Saved org '{org}' to {ConfigStore.ConfigPath}");
        }, orgOpt, patOpt, urlOpt, projOpt, descOpt);

        return cmd;
    }

    private static Command BuildRemove()
    {
        var orgArg = new Argument<string>("org", "Organization name to remove");
        var cmd = new Command("remove", "Remove an organization") { orgArg };
        cmd.SetHandler((string org) =>
        {
            var cfg = ConfigStore.Load();
            if (cfg.Organizations.Remove(org))
            {
                ConfigStore.Save(cfg);
                Console.WriteLine($"Removed '{org}'.");
            }
            else
            {
                Console.WriteLine($"Org '{org}' not found.");
            }
        }, orgArg);
        return cmd;
    }

    private static Command BuildPath()
    {
        var cmd = new Command("path", "Print path of the central config file");
        cmd.SetHandler(() => Console.WriteLine(ConfigStore.ConfigPath));
        return cmd;
    }

    private static string ReadSecret()
    {
        var sb = new System.Text.StringBuilder();
        ConsoleKeyInfo k;
        while ((k = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
        {
            if (k.Key == ConsoleKey.Backspace && sb.Length > 0) sb.Length--;
            else if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
        }
        Console.WriteLine();
        return sb.ToString();
    }
}
