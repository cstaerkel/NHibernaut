using System;
using System.IO;
using System.Runtime.InteropServices;

namespace NHibernaut.App.Logging;

/// <summary>Per-user, per-OS locations for logs and settings (XDG / Apple / Windows conventions).</summary>
public static class AppPaths
{
    private const string AppName = "NHibernaut";

    public static string LogsDirectory => EnsureDir(
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? Path.Combine(Home, "Library", "Logs", AppName)
        : RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName, "logs")
            : Path.Combine(StateHome, AppName, "logs"));

    public static string SettingsFile => Path.Combine(EnsureDir(
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? Path.Combine(Home, "Library", "Application Support", AppName)
        : RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName)
            : Path.Combine(ConfigHome, AppName)), "settings.json");

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static string StateHome => Environment.GetEnvironmentVariable("XDG_STATE_HOME") is { Length: > 0 } s ? s : Path.Combine(Home, ".local", "state");
    private static string ConfigHome => Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } c ? c : Path.Combine(Home, ".config");
    private static string EnsureDir(string dir) { Directory.CreateDirectory(dir); return dir; }
}
