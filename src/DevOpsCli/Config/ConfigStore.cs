using System.Runtime.InteropServices;
using System.Text.Json;

namespace DevOpsCli.Config;

public static class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string ConfigDir
    {
        get
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (!string.IsNullOrEmpty(xdg))
                return Path.Combine(xdg, "devops-cli");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "devops-cli");

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".config", "devops-cli");
        }
    }

    public static string ConfigPath => Path.Combine(ConfigDir, "config.json");

    public static CentralConfig Load()
    {
        if (!File.Exists(ConfigPath))
            return new CentralConfig();

        var json = File.ReadAllText(ConfigPath);
        if (string.IsNullOrWhiteSpace(json))
            return new CentralConfig();

        return JsonSerializer.Deserialize<CentralConfig>(json, JsonOpts) ?? new CentralConfig();
    }

    public static void Save(CentralConfig cfg)
    {
        Directory.CreateDirectory(ConfigDir);
        var tmp = ConfigPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(cfg, JsonOpts));

        if (File.Exists(ConfigPath)) File.Delete(ConfigPath);
        File.Move(tmp, ConfigPath);

        TryRestrictPermissions(ConfigPath);
    }

    private static void TryRestrictPermissions(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // best-effort
        }
    }
}
