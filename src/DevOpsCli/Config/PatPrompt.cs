using System.Text;

namespace DevOpsCli.Config;

public static class PatPrompt
{
    public static OrgEntry EnsureOrgRegistered(CentralConfig cfg, string org, string? detectedOrgUrl)
    {
        if (cfg.Organizations.TryGetValue(org, out var existing) && !string.IsNullOrWhiteSpace(existing.Pat))
            return existing;

        if (!Environment.UserInteractive || Console.IsInputRedirected)
            throw new InvalidOperationException(
                $"Organization '{org}' is not configured. Run: azdo config add --org {org} --pat <token>");

        Console.WriteLine();
        Console.WriteLine($"Organization '{org}' is not configured.");
        var url = detectedOrgUrl ?? $"https://dev.azure.com/{org}";
        Console.WriteLine($"Detected URL: {url}");
        Console.Write("Enter PAT (input hidden, Enter to cancel): ");

        var pat = ReadSecret();
        if (string.IsNullOrWhiteSpace(pat))
            throw new OperationCanceledException("No PAT provided.");

        Console.Write("Default project (optional, press Enter to skip): ");
        var project = Console.ReadLine();

        var entry = new OrgEntry
        {
            OrganizationUrl = url,
            Pat = pat,
            DefaultProject = string.IsNullOrWhiteSpace(project) ? null : project.Trim(),
            LastUpdated = DateTime.UtcNow
        };

        cfg.Organizations[org] = entry;
        ConfigStore.Save(cfg);
        Console.WriteLine($"Saved to {ConfigStore.ConfigPath}");
        return entry;
    }

    private static string ReadSecret()
    {
        var sb = new StringBuilder();
        ConsoleKeyInfo k;
        while ((k = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
        {
            if (k.Key == ConsoleKey.Backspace && sb.Length > 0)
                sb.Length--;
            else if (!char.IsControl(k.KeyChar))
                sb.Append(k.KeyChar);
        }
        Console.WriteLine();
        return sb.ToString();
    }
}
