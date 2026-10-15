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

/// <summary>
/// The global hotkeys, which work from any app: Ctrl+Alt+Shift with a letter. Ctrl+Alt+Shift leaves the keys JAWS, NVDA
/// and Narrator use (Insert, Caps Lock, Windows) alone.
/// </summary>
public sealed class HotKeys : IDisposable
{
    public const string Modifiers = "Ctrl+Alt+Shift";

    public static readonly (Target Target, char Key)[] All =
    [
        (Target.Screen, 'S'),
        (Target.Window, 'W'),
        (Target.Focus, 'F'),
        (Target.Clipboard, 'V'),
    ];

    public static string GestureFor(Target target) => $"{Modifiers}+{All.First(h => h.Target == target).Key}";

    private const int FirstId = 0x5144; // "QD"
    private readonly HwndSource _source;
    private readonly List<int> _registered = [];

    public event Action<Target>? Pressed;

    /// <summary>The hotkeys another app already has, so they couldn't be registered.</summary>
    public IReadOnlyList<Target> Unavailable { get; }

    public HotKeys(HwndSource source)
    {
        _source = source;
        _source.AddHook(Hook);
        var unavailable = new List<Target>();
        for (var i = 0; i < All.Length; i++)
        {
            var id = FirstId + i;
            if (Native.RegisterHotKey(source.Handle, id, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_SHIFT | Native.MOD_NOREPEAT, All[i].Key))
                _registered.Add(id);
            else
                unavailable.Add(All[i].Target);
        }
        Unavailable = unavailable;
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY)
        {
            var index = wParam.ToInt32() - FirstId;
            if (index >= 0 && index < All.Length)
            {
                handled = true;
                Pressed?.Invoke(All[index].Target);
            }
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
