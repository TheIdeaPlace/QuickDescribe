using System.Text.Json;

namespace QuickDescribe.Services;

/// <summary>The choices kept between runs, in %LOCALAPPDATA%\Quick Describe\settings.json.</summary>
public sealed class Settings
{
    /// <summary>The description kind: one of <see cref="WindowsAI.Kinds"/>.</summary>
    public string Kind { get; set; } = WindowsAI.Kinds[0].Id;

    internal static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.DataFolderName, "settings.json");

    /// <summary>The saved settings, or the defaults when there are none or they can't be read.</summary>
    public static Settings Load(string? path = null)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path ?? DefaultPath)) ?? new Settings();
            if (!WindowsAI.Kinds.Any(k => k.Id == settings.Kind)) settings.Kind = WindowsAI.Kinds[0].Id;
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Settings();
        }
    }

    /// <summary>Saves, quietly: losing a setting isn't worth interrupting anyone over.</summary>
    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
