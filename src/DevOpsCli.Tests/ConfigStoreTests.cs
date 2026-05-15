using DevOpsCli.Config;

namespace DevOpsCli.Tests;

/// <summary>
/// Tests for ConfigStore. Uses XDG_CONFIG_HOME to isolate each test from the real user config.
/// Tests in this class run sequentially (same class → xUnit default = sequential within a class).
/// </summary>
public class ConfigStoreTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly string? _savedXdg;

    public ConfigStoreTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "azdo-cfgtest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
        _savedXdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _tmpDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _savedXdg);
        try { Directory.Delete(_tmpDir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaultConfig()
    {
        var cfg = ConfigStore.Load();
        Assert.NotNull(cfg);
        Assert.Equal("7.1", cfg.DefaultApiVersion);
        Assert.Equal(30, cfg.TimeoutSeconds);
        Assert.Empty(cfg.Organizations);
    }

    [Fact]
    public void SaveLoad_RoundTrip_PreservesAllFields()
    {
        var cfg = new CentralConfig
        {
            DefaultApiVersion = "6.0",
            TimeoutSeconds = 60
        };
        cfg.Organizations["myorg"] = new OrgEntry
        {
            OrganizationUrl = "https://dev.azure.com/myorg",
            Pat = "myPAT",
            DefaultProject = "myproject",
            Description = "test org"
        };

        ConfigStore.Save(cfg);
        var loaded = ConfigStore.Load();

        Assert.Equal("6.0", loaded.DefaultApiVersion);
        Assert.Equal(60, loaded.TimeoutSeconds);
        Assert.True(loaded.Organizations.ContainsKey("myorg"));
        var org = loaded.Organizations["myorg"];
        Assert.Equal("myPAT", org.Pat);
        Assert.Equal("https://dev.azure.com/myorg", org.OrganizationUrl);
        Assert.Equal("myproject", org.DefaultProject);
        Assert.Equal("test org", org.Description);
    }

    [Fact]
    public void Save_AtomicWrite_TmpFileRemovedAfterSave()
    {
        ConfigStore.Save(new CentralConfig());

        Assert.False(File.Exists(ConfigStore.ConfigPath + ".tmp"), "Temporary .tmp file should not exist after save");
        Assert.True(File.Exists(ConfigStore.ConfigPath), "Config file should exist after save");
    }

    [Fact]
    public void Load_EmptyJson_ReturnsDefaultConfig()
    {
        Directory.CreateDirectory(ConfigStore.ConfigDir);
        File.WriteAllText(ConfigStore.ConfigPath, "");

        var cfg = ConfigStore.Load();
        Assert.NotNull(cfg);
    }

    [Fact]
    public void Save_UnixPermissions_AreRestrictedOnLinuxFilesystem()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return; // permissions test only meaningful on Unix

        // Redirect to /tmp (ext4) to guarantee SetUnixFileMode works
        var linuxTmp = Path.Combine("/tmp", "azdo-perm-" + Guid.NewGuid().ToString("N"));
        var priorXdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", linuxTmp);
        try
        {
            Directory.CreateDirectory(linuxTmp);
            ConfigStore.Save(new CentralConfig());

            var mode = File.GetUnixFileMode(ConfigStore.ConfigPath);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", priorXdg);
            try { Directory.Delete(linuxTmp, recursive: true); } catch { }
        }
    }
}
