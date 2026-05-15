using DevOpsCli.Config;
using DevOpsCli.Context;

namespace DevOpsCli.AzureDevOps;

/// <summary>
/// Represents an active Azure DevOps session. Owns the lifetime of <see cref="Client"/>.
/// </summary>
/// <remarks>
/// ⚠️ Do NOT use the <c>with</c> expression to clone a Session.
/// The copy would share the same <see cref="AzureDevOpsClient"/> instance;
/// disposing either copy would invalidate the other's HttpClient.
/// </remarks>
public sealed record Session(AzureDevOpsClient Client, string Org, string? Project, DetectedContext? Detected)
    : IDisposable
{
    public void Dispose() => Client.Dispose();
}

public static class ClientFactory
{
    public static Session OpenSession(string? orgOverride = null, string? projectOverride = null)
    {
        var cfg = ConfigStore.Load();

        string org;
        string? project;
        DetectedContext? detected = null;
        string? detectedUrl = null;

        if (!string.IsNullOrWhiteSpace(orgOverride))
        {
            org = orgOverride!;
            project = projectOverride;
        }
        else
        {
            detected = OrgDetector.Detect()
                ?? throw new InvalidOperationException(
                    "Could not detect Azure DevOps org from git remote. " +
                    "Run inside a repo with an Azure DevOps remote, or pass --org.");
            org = detected.Org;
            project = projectOverride ?? detected.Project;
            detectedUrl = detected.OrgUrl;
        }

        var entry = PatPrompt.EnsureOrgRegistered(cfg, org, detectedUrl);

        if (string.IsNullOrWhiteSpace(project))
            project = entry.DefaultProject;

        var client = new AzureDevOpsClient(entry, cfg.DefaultApiVersion, cfg.TimeoutSeconds);
        return new Session(client, org, project, detected);
    }
}
