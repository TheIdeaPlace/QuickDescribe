using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using QuickDescribe.Services;

namespace QuickDescribe.Views;

/// <summary>
/// File > Settings: the keys held with every hotkey (check boxes), each command's letter (A to Z, or None to turn it off),
/// and the description type. OK checks the choices (<see cref="HotKeyMap.Problems"/>) and then tries them with Windows:
/// a combination another app already has is said, and OK pressed again keeps it anyway (the Describe menu still works).
/// </summary>
public partial class SettingsWindow : Window
{
    public const string NoneItem = "None";

    private readonly Func<HotKeyMap, IReadOnlyList<Target>> _apply;
    private readonly HotKeyMap _opened;
    private HotKeyMap? _warnedAbout;

    /// <summary>The control a message last went to, as its help text: cleared when anything changes.</summary>
    private Control? _toldOn;

    /// <summary>The choices, once OK has been pressed and they've been applied.</summary>
    public HotKeyMap Map { get; private set; }
    public string Kind { get; private set; }

    /// <param name="apply">Puts a map into use and says which of its hotkeys Windows refused (another app has them).</param>
    public SettingsWindow(HotKeyMap map, string kind, Func<HotKeyMap, IReadOnlyList<Target>> apply)
    {
        InitializeComponent();
        _apply = apply;
        _opened = map;
        Map = map;
        Kind = kind;
        var letters = new[] { NoneItem }.Concat(Enumerable.Range('A', 26).Select(c => ((char)c).ToString())).ToList();
        foreach (var box in Keys) box.ItemsSource = letters;
        foreach (var (id, title) in WindowsAI.Kinds) KindBox.Items.Add(new ComboBoxItem { Content = title, Tag = id });
        Show(map, kind);
        Loaded += (_, _) => CtrlBox.Focus();
    }

    private ComboBox[] Keys => [ScreenKey, WindowKey, FocusKey, ClipboardKey];

    private ComboBox KeyFor(Target target) => Keys[Array.IndexOf(HotKeyMap.Targets, target)];

    private bool _showing;

    private void Show(HotKeyMap map, string kind)
    {
        _showing = true;
        CtrlBox.IsChecked = map.Modifiers.HasFlag(KeyModifiers.Ctrl);
        AltBox.IsChecked = map.Modifiers.HasFlag(KeyModifiers.Alt);
        ShiftBox.IsChecked = map.Modifiers.HasFlag(KeyModifiers.Shift);
        WindowsBox.IsChecked = map.Modifiers.HasFlag(KeyModifiers.Windows);
        ConflictsBox.IsChecked = map.AllowConflicts;
        foreach (var target in HotKeyMap.Targets) KeyFor(target).SelectedItem = map.LetterFor(target)?.ToString() ?? NoneItem;
        KindBox.SelectedItem = KindBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == kind) ?? KindBox.Items[0];
        _showing = false;
        Summarize();
    }

    /// <summary>The map the controls show.</summary>
    internal HotKeyMap Chosen()
    {
        var modifiers = KeyModifiers.None;
        if (CtrlBox.IsChecked == true) modifiers |= KeyModifiers.Ctrl;
        if (AltBox.IsChecked == true) modifiers |= KeyModifiers.Alt;
        if (ShiftBox.IsChecked == true) modifiers |= KeyModifiers.Shift;
        if (WindowsBox.IsChecked == true) modifiers |= KeyModifiers.Windows;
        return new HotKeyMap(modifiers, HotKeyMap.Targets.ToDictionary(t => t,
            t => KeyFor(t).SelectedItem is string s && s != NoneItem ? s[0] : (char?)null), ConflictsBox.IsChecked == true);
    }

    private void Changed(object sender, RoutedEventArgs e)
    {
        if (_showing) return;
        _warnedAbout = null;
        if (_toldOn is not null) AutomationProperties.SetHelpText(_toldOn, "");
        _toldOn = null;
        Summarize();
    }

    /// <summary>What the choices add up to, or what's wrong: shown, and the help text of OK so it's heard there.</summary>
    private void Summarize()
    {
        var map = Chosen();
        var problems = map.Problems();
        SummaryText.Text = problems.Count > 0 ? string.Join(" ", problems) : Summary(map);
        AutomationProperties.SetHelpText(OkButton, SummaryText.Text);
    }

    internal static string Summary(HotKeyMap map)
    {
        var on = HotKeyMap.Targets.Where(t => map.GestureFor(t) is not null).Select(t => $"{HotKeyMap.Title(t)}, {map.GestureFor(t)}").ToList();
        return on.Count == 0 ? "No hotkeys: the Describe and File menus still do everything." : string.Join(". ", on) + ".";
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        Show(HotKeyMap.Default, WindowsAI.Kinds[0].Id);
        _warnedAbout = null;
        Announcer.Announce($"Back to the defaults. {SummaryText.Text}");
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var map = Chosen();
        if (map.Problems() is { Count: > 0 } problems)
        {
            Tell(string.Join(" ", problems), map.Problems().Any(p => p.StartsWith("Hold", StringComparison.Ordinal)) ? CtrlBox : FirstDuplicate(map));
            return;
        }
        Kind = (string)((ComboBoxItem)KindBox.SelectedItem).Tag;
        // Tried with Windows now, so a combination another app has is said here, where it can be changed.
        var refused = _apply(map);
        Map = map;
        if (ShouldWarn(_opened, map, refused, _warnedAbout))
        {
            _warnedAbout = map;
            Tell($"{map.Refused(refused)} Choose another letter, or press OK again to keep {(refused.Count == 1 ? "it" : "them")} anyway.",
                KeyFor(refused[0]));
            return;
        }
        DialogResult = true;
    }

    /// <summary>Whether OK says some hotkeys are taken rather than closing: only for hotkeys that changed (the ones in use
    /// were said at the start), and only once for the same choice, so OK again keeps them.</summary>
    internal static bool ShouldWarn(HotKeyMap opened, HotKeyMap chosen, IReadOnlyList<Target> refused, HotKeyMap? warnedAbout) =>
        refused.Count > 0 && !chosen.SameAs(opened) && (warnedAbout is null || !warnedAbout.SameAs(chosen));

    /// <summary>The list to fix when two commands share a letter: the one changed since Settings opened (the letter just
    /// chosen), or else the later one.</summary>
    private Control FirstDuplicate(HotKeyMap map) =>
        HotKeyMap.Targets.Where(t => map.LetterFor(t) is not null).GroupBy(t => map.LetterFor(t)).FirstOrDefault(g => g.Count() > 1) is { } same
            ? KeyFor(same.FirstOrDefault(t => map.LetterFor(t) != _opened.LetterFor(t), same.Last()))
            : CtrlBox;

    /// <summary>Says what's wrong in a message box, logs it, and then puts focus where it can be fixed (with the message
    /// as that control's help text too).</summary>
    private void Tell(string message, Control fix)
    {
        Log.Write("settings", $"OK didn't save: {message}");
        SummaryText.Text = message;
        AutomationProperties.SetHelpText(OkButton, message);
        if (_toldOn is not null) AutomationProperties.SetHelpText(_toldOn, "");
        AutomationProperties.SetHelpText(fix, message);
        _toldOn = fix;
        // A message box, which JAWS and NVDA both read as it opens. Help text alone wasn't enough: NVDA reads it with the
        // control focus moves to, JAWS doesn't, so with JAWS pressing OK seemed to do nothing (Kelly, 16 Oct 2026). Closing
        // it puts focus where the fix goes.
        Problem?.Invoke(this, message);
        fix.Focus();
    }

    /// <summary>Shows why OK didn't close; replaceable in tests.</summary>
    internal static Action<Window, string>? Problem { get; set; } =
        (owner, message) => MessageBox.Show(owner, message, "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
}
