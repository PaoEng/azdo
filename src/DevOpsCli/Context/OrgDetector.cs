using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DevOpsCli.Context;

public sealed record DetectedContext(string Org, string OrgUrl, string? Project, string? Repo, string Source);

public static class OrgDetector
{
    public static DetectedContext? Detect(string? startDir = null)
    {
        var dir = startDir ?? Directory.GetCurrentDirectory();
        var repoRoot = FindGitRoot(dir);
        if (repoRoot is null) return null;

        var remote = RunGit(repoRoot, "remote", "get-url", "origin");
        if (string.IsNullOrWhiteSpace(remote)) return null;

        return ParseRemote(remote.Trim());
    }

    public static string? CurrentBranch(string? startDir = null)
    {
        var dir = startDir ?? Directory.GetCurrentDirectory();
        var repoRoot = FindGitRoot(dir);
        if (repoRoot is null) return null;
        var branch = RunGit(repoRoot, "rev-parse", "--abbrev-ref", "HEAD");
        if (string.IsNullOrWhiteSpace(branch)) return null;
        branch = branch.Trim();
        return branch == "HEAD" ? null : branch; // detached HEAD
    }

    public static DetectedContext? ParseRemote(string remote)
    {
        // https://dev.azure.com/{org}/{project}/_git/{repo}
        var m = Regex.Match(remote,
            @"^https?://(?:[^@/]+@)?dev\.azure\.com/(?<org>[^/]+)/(?<project>[^/]+)/_git/(?<repo>[^/?#]+)",
            RegexOptions.IgnoreCase);
        if (m.Success)
            return new DetectedContext(
                m.Groups["org"].Value,
                $"https://dev.azure.com/{m.Groups["org"].Value}",
                Decode(m.Groups["project"].Value),
                Decode(m.Groups["repo"].Value).TrimEnd('/').Replace(".git", ""),
                "https-devazure");

        // https://{org}@dev.azure.com/{org}/{project}/_git/{repo}
        // (already handled by the user@ optional above)

        // https://{org}.visualstudio.com/{project}/_git/{repo}
        m = Regex.Match(remote,
            @"^https?://(?<org>[^.]+)\.visualstudio\.com/(?:DefaultCollection/)?(?<project>[^/]+)/_git/(?<repo>[^/?#]+)",
            RegexOptions.IgnoreCase);
        if (m.Success)
            return new DetectedContext(
                m.Groups["org"].Value,
                $"https://dev.azure.com/{m.Groups["org"].Value}",
                Decode(m.Groups["project"].Value),
                Decode(m.Groups["repo"].Value).TrimEnd('/').Replace(".git", ""),
                "https-visualstudio");

        // git@ssh.dev.azure.com:v3/{org}/{project}/{repo}
        m = Regex.Match(remote,
            @"^git@ssh\.dev\.azure\.com:v3/(?<org>[^/]+)/(?<project>[^/]+)/(?<repo>[^/?#]+)",
            RegexOptions.IgnoreCase);
        if (m.Success)
            return new DetectedContext(
                m.Groups["org"].Value,
                $"https://dev.azure.com/{m.Groups["org"].Value}",
                Decode(m.Groups["project"].Value),
                Decode(m.Groups["repo"].Value).TrimEnd('/').Replace(".git", ""),
                "ssh-devazure");

        // {org}@vs-ssh.visualstudio.com:v3/{org}/{project}/{repo}
        m = Regex.Match(remote,
            @"^[^@]+@vs-ssh\.visualstudio\.com:v3/(?<org>[^/]+)/(?<project>[^/]+)/(?<repo>[^/?#]+)",
            RegexOptions.IgnoreCase);
        if (m.Success)
            return new DetectedContext(
                m.Groups["org"].Value,
                $"https://dev.azure.com/{m.Groups["org"].Value}",
                Decode(m.Groups["project"].Value),
                Decode(m.Groups["repo"].Value).TrimEnd('/').Replace(".git", ""),
                "ssh-visualstudio");

        return null;
    }

    private static string Decode(string s) => Uri.UnescapeDataString(s);

    private static string? FindGitRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) ||
                File.Exists(Path.Combine(dir.FullName, ".git")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private static string? RunGit(string workingDir, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            // Bypass "dubious ownership" check (es. WSL su mount NTFS dove l'UID
            // del proprietario non corrisponde). Scoped a questa invocazione:
            // non tocca la config globale dell'utente.
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add($"safe.directory={workingDir}");
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var p = Process.Start(psi);
            if (p is null) return null;
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            if (p.ExitCode == 0) return stdout;

            var stderr = p.StandardError.ReadToEnd();
            if (!string.IsNullOrWhiteSpace(stderr))
                Console.Error.WriteLine($"[azdo] git {string.Join(' ', args)} failed: {stderr.Trim()}");
            return null;
        }
        catch
        {
            return null;
        }
    }
}
