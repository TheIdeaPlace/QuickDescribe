using System.Text.Json;

namespace QuickDescribe.Services;

/// <summary>The choices kept between runs, and what the update checks remember, in the data folder's settings.json.</summary>
public sealed class Settings
{
    /// <summary>The description kind: one of <see cref="WindowsAI.Kinds"/>.</summary>
    public string Kind { get; set; } = WindowsAI.Kinds[0].Id;

    /// <summary>The hotkeys' modifiers (Win32 MOD_ flags) and each command's letter ("" for none), as chosen in Settings;
    /// null until then, for the defaults. Read through <see cref="HotKeys"/>.</summary>
    public int? HotKeyModifiers { get; set; }
    public Dictionary<string, string>? HotKeyLetters { get; set; }

    /// <summary>Settings' "Allow key combinations other apps use" (Ctrl+Shift, Ctrl or Alt alone): off unless chosen.</summary>
    public bool HotKeysAllowConflicts { get; set; }

    /// <summary>The hotkeys to use: the ones chosen, or the defaults when none were or they can't be used.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public HotKeyMap HotKeys
    {
        get => HotKeyMap.FromSettings(HotKeyModifiers, HotKeyLetters, HotKeysAllowConflicts);
        set => (HotKeyModifiers, HotKeyLetters, HotKeysAllowConflicts) = value.ToSettings();
    }

    /// <summary>Settings' "Keep running in the notification area when I close the window": closing then hides the window
    /// and the hotkeys keep working, until File, Exit. Off unless chosen.</summary>
    public bool KeepRunning { get; set; }

    /// <summary>The first close with Keep running on says where Quick Describe went; once.</summary>
    public bool KeepRunningSaid { get; set; }

    /// <summary>When updates were last looked for, so the quiet check runs at most once a day.</summary>
    public DateTime? LastUpdateCheck { get; set; }

    /// <summary>An update found and not installed yet, so Help still says so after a restart.</summary>
    public string? AvailableUpdate { get; set; }

    /// <summary>The installed version that ran last, so the first run after an update can say so.</summary>
    public string? LastRunVersion { get; set; }

    internal static string DefaultPath => Path.Combine(AppPaths.DataFolder, "settings.json");

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
