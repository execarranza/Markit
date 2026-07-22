using System.IO;
using System.Text.Json;

namespace ReadmeReader;

internal sealed class UserSettings
{
    private const int MaxRecentFiles = 8;

    public bool IsDarkMode { get; set; }

    public double Zoom { get; set; } = 100;

    public double WindowWidth { get; set; } = 1180;

    public double WindowHeight { get; set; } = 780;

    public List<string> RecentFiles { get; set; } = [];

    private static string SettingsPath
    {
        get
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Markit");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "settings.json");
        }
    }

    public static UserSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new UserSettings();
            }

            var settings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(SettingsPath));
            return settings?.Sanitize() ?? new UserSettings();
        }
        catch
        {
            return new UserSettings();
        }
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(
                SettingsPath,
                JsonSerializer.Serialize(Sanitize(), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Settings should never block reading or saving Markdown documents.
        }
    }

    public void AddRecentFile(string fileName)
    {
        var fullPath = Path.GetFullPath(fileName);
        RecentFiles.RemoveAll(file => string.Equals(file, fullPath, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, fullPath);
        RecentFiles = RecentFiles
            .Where(file => !string.IsNullOrWhiteSpace(file))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxRecentFiles)
            .ToList();
    }

    public void RemoveRecentFile(string fileName)
    {
        RecentFiles.RemoveAll(file => string.Equals(file, fileName, StringComparison.OrdinalIgnoreCase));
    }

    private UserSettings Sanitize()
    {
        Zoom = Math.Clamp(Zoom, 50, 300);
        WindowWidth = Math.Max(860, WindowWidth);
        WindowHeight = Math.Max(560, WindowHeight);
        RecentFiles = RecentFiles
            .Where(file => !string.IsNullOrWhiteSpace(file))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxRecentFiles)
            .ToList();
        return this;
    }
}
