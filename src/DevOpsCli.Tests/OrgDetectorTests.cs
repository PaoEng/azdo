using DevOpsCli.Context;

namespace DevOpsCli.Tests;

public class OrgDetectorTests
{
    // ── Format 1: https://dev.azure.com/{org}/{project}/_git/{repo} ──────────

    [Fact]
    public void ParseRemote_HttpsDevAzureCom_ReturnsCorrectContext()
    {
        var r = OrgDetector.ParseRemote("https://dev.azure.com/myorg/myproject/_git/myrepo");
        Assert.NotNull(r);
        Assert.Equal("myorg", r.Org);
        Assert.Equal("https://dev.azure.com/myorg", r.OrgUrl);
        Assert.Equal("myproject", r.Project);
        Assert.Equal("myrepo", r.Repo);
        Assert.Equal("https-devazure", r.Source);
    }

    [Fact]
    public void ParseRemote_HttpsDevAzureComWithUserAt_ReturnsCorrectContext()
    {
        var r = OrgDetector.ParseRemote("https://user@dev.azure.com/myorg/myproject/_git/myrepo");
        Assert.NotNull(r);
        Assert.Equal("myorg", r.Org);
        Assert.Equal("myproject", r.Project);
        Assert.Equal("myrepo", r.Repo);
        Assert.Equal("https-devazure", r.Source);
    }

    // ── Format 2: https://{org}.visualstudio.com/{project}/_git/{repo} ───────

    [Fact]
    public void ParseRemote_HttpsVisualStudioCom_ReturnsCorrectContext()
    {
        var r = OrgDetector.ParseRemote("https://myorg.visualstudio.com/myproject/_git/myrepo");
        Assert.NotNull(r);
        Assert.Equal("myorg", r.Org);
        Assert.Equal("https://dev.azure.com/myorg", r.OrgUrl);
        Assert.Equal("myproject", r.Project);
        Assert.Equal("myrepo", r.Repo);
        Assert.Equal("https-visualstudio", r.Source);
    }

    [Fact]
    public void ParseRemote_HttpsVisualStudioComDefaultCollection_ReturnsCorrectContext()
    {
        var r = OrgDetector.ParseRemote("https://myorg.visualstudio.com/DefaultCollection/myproject/_git/myrepo");
        Assert.NotNull(r);
        Assert.Equal("myorg", r.Org);
        Assert.Equal("myproject", r.Project);
        Assert.Equal("myrepo", r.Repo);
        Assert.Equal("https-visualstudio", r.Source);
    }

    // ── Format 3: git@ssh.dev.azure.com:v3/{org}/{project}/{repo} ────────────

    [Fact]
    public void ParseRemote_SshDevAzureCom_ReturnsCorrectContext()
    {
        var r = OrgDetector.ParseRemote("git@ssh.dev.azure.com:v3/myorg/myproject/myrepo");
        Assert.NotNull(r);
        Assert.Equal("myorg", r.Org);
        Assert.Equal("https://dev.azure.com/myorg", r.OrgUrl);
        Assert.Equal("myproject", r.Project);
        Assert.Equal("myrepo", r.Repo);
        Assert.Equal("ssh-devazure", r.Source);
    }

    // ── Format 4: {org}@vs-ssh.visualstudio.com:v3/{org}/{project}/{repo} ────

    [Fact]
    public void ParseRemote_SshVisualStudioCom_ReturnsCorrectContext()
    {
        var r = OrgDetector.ParseRemote("myorg@vs-ssh.visualstudio.com:v3/myorg/myproject/myrepo");
        Assert.NotNull(r);
        Assert.Equal("myorg", r.Org);
        Assert.Equal("https://dev.azure.com/myorg", r.OrgUrl);
        Assert.Equal("myproject", r.Project);
        Assert.Equal("myrepo", r.Repo);
        Assert.Equal("ssh-visualstudio", r.Source);
    }

    // ── Malformed / unrecognised remotes ──────────────────────────────────────

    [Theory]
    [InlineData("https://github.com/user/repo")]
    [InlineData("not-a-url")]
    [InlineData("")]
    [InlineData("https://dev.azure.com/onlyone")]
    public void ParseRemote_Malformed_ReturnsNull(string remote)
    {
        Assert.Null(OrgDetector.ParseRemote(remote));
    }

    // ── URL-encoded names ─────────────────────────────────────────────────────

    [Fact]
    public void ParseRemote_UrlEncodedNames_DecodesCorrectly()
    {
        var r = OrgDetector.ParseRemote("https://dev.azure.com/myorg/my%20project/_git/my%20repo");
        Assert.NotNull(r);
        Assert.Equal("my project", r.Project);
        Assert.Equal("my repo", r.Repo);
    }

    // ── Detect() without a git repository ────────────────────────────────────

    [Fact]
    public void Detect_NoGitRepo_ReturnsNull()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            Assert.Null(OrgDetector.Detect(tmpDir));
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }
}
