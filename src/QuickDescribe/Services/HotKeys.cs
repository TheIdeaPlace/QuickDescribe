using System.Windows.Interop;

namespace QuickDescribe.Services;

/// <summary>What a global hotkey does.</summary>
public enum Target
{
    Screen,
    Window,
    Focus,
    Clipboard,
}

/// <summary>The keys held with a hotkey's letter. The values are Win32's MOD_ flags.</summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Alt = 0x1,
    Ctrl = 0x2,
    Shift = 0x4,
    Windows = 0x8,
}

/// <summary>
/// Which keys do what, from any app: the modifiers every hotkey holds, and each command's letter (or none, to turn that
/// hotkey off). Kept in settings and chosen in Settings. The default, Ctrl+Alt+Shift, leaves the keys JAWS, NVDA and
/// Narrator use (Insert, Caps Lock, Windows) alone.
/// </summary>
public sealed record HotKeyMap(KeyModifiers Modifiers, IReadOnlyDictionary<Target, char?> Letters)
{
    public static readonly HotKeyMap Default = new(KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Shift,
        new Dictionary<Target, char?> { [Target.Screen] = 'S', [Target.Window] = 'W', [Target.Focus] = 'F', [Target.Clipboard] = 'V' });

    /// <summary>The commands, in the order they're listed everywhere.</summary>
    public static readonly Target[] Targets = [Target.Screen, Target.Window, Target.Focus, Target.Clipboard];

    /// <summary>What each command is called, as in the Describe and File menus.</summary>
    public static string Title(Target target) => target switch
    {
        Target.Screen => "Screen",
        Target.Window => "Active window",
        Target.Focus => "Focused item",
        _ => "Clipboard picture",
    };

    /// <summary>"Ctrl+Alt+Shift", in Windows' usual order.</summary>
    public string ModifierText => string.Join("+", new[]
    {
        Modifiers.HasFlag(KeyModifiers.Ctrl) ? "Ctrl" : null,
        Modifiers.HasFlag(KeyModifiers.Alt) ? "Alt" : null,
        Modifiers.HasFlag(KeyModifiers.Shift) ? "Shift" : null,
        Modifiers.HasFlag(KeyModifiers.Windows) ? "Windows" : null,
    }.Where(m => m is not null));

    public char? LetterFor(Target target) => Letters.TryGetValue(target, out var letter) ? letter : null;

    /// <summary>"Ctrl+Alt+Shift+S", or null when the command has no hotkey.</summary>
    public string? GestureFor(Target target) => LetterFor(target) is { } letter ? $"{ModifierText}+{letter}" : null;

    /// <summary>What's wrong with this map, for the person to fix; empty when it can be used.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (!ModifiersAreSafe(Modifiers))
            problems.Add("Hold at least two of Ctrl, Alt and the Windows key, or the Windows key: with Ctrl or Alt alone (with or without Shift), " +
                         "or Shift alone, the hotkeys would take keys like Ctrl+V, Ctrl+S and menus' Alt keys away from every app.");
        foreach (var letter in Letters.Values.OfType<char>())
            if (letter is not (>= 'A' and <= 'Z'))
                problems.Add($"{letter} isn't a letter from A to Z.");
        foreach (var same in Targets.Where(t => LetterFor(t) is not null).GroupBy(t => LetterFor(t)).Where(g => g.Count() > 1))
            problems.Add($"{Joined(same.Select(Title))} {(same.Count() == 2 ? "both use" : "all use")} {same.Key}. Give each its own letter.");
        return problems;
    }

    /// <summary>
    /// Whether hotkeys held with these keys leave other apps' shortcuts alone. A global hotkey wins over every app's own
    /// keys, so Ctrl alone would take Paste, Save and Find, Alt alone the menus' access keys, Ctrl+Shift Save As and the
    /// like. Two of Ctrl, Alt and Windows, or Windows (which Windows keeps for itself, so it reserves its own), are safe.
    /// </summary>
    public static bool ModifiersAreSafe(KeyModifiers modifiers)
    {
        if (modifiers.HasFlag(KeyModifiers.Windows)) return true;
        return modifiers.HasFlag(KeyModifiers.Ctrl) && modifiers.HasFlag(KeyModifiers.Alt);
    }

    /// <summary>This map as kept in settings.</summary>
    public (int Modifiers, Dictionary<string, string> Letters) ToSettings() =>
        ((int)Modifiers, Targets.ToDictionary(t => t.ToString(), t => LetterFor(t)?.ToString() ?? ""));

    /// <summary>The map kept in settings, or the default when there's none or it can't be used (logged, so a choice
    /// that stopped being allowed doesn't vanish without a word).</summary>
    public static HotKeyMap FromSettings(int? modifiers, IReadOnlyDictionary<string, string>? letters)
    {
        var map = Read(modifiers, letters);
        if (map is null) return Default;
        if (map.Problems() is { Count: > 0 } problems)
        {
            Log.Write("settings", $"The saved hotkeys can't be used, so the defaults are: {string.Join(" ", problems)}");
            return Default;
        }
        return map;
    }

    private static HotKeyMap? Read(int? modifiers, IReadOnlyDictionary<string, string>? letters)
    {
        if (modifiers is null || letters is null) return null;
        return new HotKeyMap((KeyModifiers)modifiers.Value & (KeyModifiers.Alt | KeyModifiers.Ctrl | KeyModifiers.Shift | KeyModifiers.Windows),
            Targets.ToDictionary(t => t, t => letters.TryGetValue(t.ToString(), out var l) && l.Trim() is { Length: 1 } one
                ? char.ToUpperInvariant(one[0]) : (char?)null));
    }

    /// <summary>"A, B and C".</summary>
    public static string Joined(IEnumerable<string?> items)
    {
        var list = items.OfType<string>().ToList();
        return list.Count <= 1 ? string.Concat(list) : $"{string.Join(", ", list.Take(list.Count - 1))} and {list[^1]}";
    }

    /// <summary>What's said when Windows refused some hotkeys: another app has them, or Windows keeps them for itself (as it
    /// does many Windows key combinations).</summary>
    public string Refused(IReadOnlyList<Target> refused) =>
        $"{Joined(refused.Select(GestureFor))} {(refused.Count == 1 ? "is" : "are")} already taken, by another app or by Windows itself, " +
        $"so {(refused.Count == 1 ? "it doesn't" : "they don't")} work from other apps.";

    public bool SameAs(HotKeyMap other) => Modifiers == other.Modifiers && Targets.All(t => LetterFor(t) == other.LetterFor(t));
}

/// <summary>The global hotkeys of a <see cref="HotKeyMap"/>, registered with Windows for this window while they last.</summary>
public sealed class HotKeys : IDisposable
{
    private const int FirstId = 0x5144; // "QD"
    private readonly HwndSource _source;
    private readonly List<int> _registered = [];

    public event Action<Target>? Pressed;

    public HotKeyMap Map { get; }

    /// <summary>The hotkeys another app (or Windows) already has, so they couldn't be registered.</summary>
    public IReadOnlyList<Target> Unavailable { get; }

    public HotKeys(HwndSource source, HotKeyMap map)
    {
        _source = source;
        Map = map;
        _source.AddHook(Hook);
        var unavailable = new List<Target>();
        foreach (var target in HotKeyMap.Targets)
        {
            if (map.LetterFor(target) is not { } letter) continue;
            var id = FirstId + (int)target;
            if (Native.RegisterHotKey(source.Handle, id, (uint)map.Modifiers | Native.MOD_NOREPEAT, letter))
                _registered.Add(id);
            else
                unavailable.Add(target);
        }
        Unavailable = unavailable;
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && _registered.Contains(wParam.ToInt32()))
        {
            handled = true;
            Pressed?.Invoke((Target)(wParam.ToInt32() - FirstId));
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _registered) Native.UnregisterHotKey(_source.Handle, id);
        _registered.Clear();
        _source.RemoveHook(Hook);
    }
}
