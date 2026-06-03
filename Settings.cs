using System.IO;
using System.Text.Json;

namespace FileUnlocker;

internal static class Settings
{
    public enum Language { Zh, En }
    public enum AppTheme { System, Light, Dark }

    public static Language Lang { get; set; } = Language.Zh;
    public static AppTheme Theme { get; set; } = AppTheme.System;
    public static RestartManager.ScanDepth ScanDepth { get; set; } = RestartManager.ScanDepth.Recursive;
    public static bool IgnoreGit { get; set; } = true;

    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FileUnlocker", "settings.json");

    public static void Load()
    {
        try
        {
            if (!File.Exists(Path)) return;
            var json = File.ReadAllText(Path);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("lang", out var lang))
                Lang = lang.GetString() == "en" ? Language.En : Language.Zh;
            if (root.TryGetProperty("theme", out var theme))
                Theme = theme.GetString() switch { "light" => AppTheme.Light, "dark" => AppTheme.Dark, _ => AppTheme.System };
            if (root.TryGetProperty("scanDepth", out var sd))
                ScanDepth = sd.GetInt32() switch { 1 => RestartManager.ScanDepth.OneLevel, 2 => RestartManager.ScanDepth.Recursive, _ => RestartManager.ScanDepth.CurrentOnly };
            if (root.TryGetProperty("ignoreGit", out var ig))
                IgnoreGit = ig.GetBoolean();
        }
        catch { }
    }

    public static void Save()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(Path)!;
            Directory.CreateDirectory(dir);
            var themeStr = Theme switch { AppTheme.Light => "light", AppTheme.Dark => "dark", _ => "system" };
            var json = $"{{\"lang\":\"{(Lang == Language.En ? "en" : "zh")}\",\"theme\":\"{themeStr}\",\"scanDepth\":{(int)ScanDepth},\"ignoreGit\":{IgnoreGit.ToString().ToLower()}}}";
            File.WriteAllText(Path, json);
        }
        catch { }
    }
}
