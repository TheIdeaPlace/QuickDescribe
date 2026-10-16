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
    private HotKeyMap? _warnedAbout;

    /// <summary>The choices, once OK has been pressed and they've been applied.</summary>
    public HotKeyMap Map { get; private set; }
    public string Kind { get; private set; }

    /// <param name="apply">Puts a map into use and says which of its hotkeys Windows refused (another app has them).</param>
    public SettingsWindow(HotKeyMap map, string kind, Func<HotKeyMap, IReadOnlyList<Target>> apply)
    {
        InitializeComponent();
        _apply = apply;
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
            t => KeyFor(t).SelectedItem is string s && s != NoneItem ? s[0] : (char?)null));
    }

    private void Changed(object sender, RoutedEventArgs e)
    {
        if (_showing) return;
        _warnedAbout = null;
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
            Tell(string.Join(" ", problems), problems[0].StartsWith("Hold", StringComparison.Ordinal) ? CtrlBox : FirstDuplicate(map));
            return;
        }
        Kind = (string)((ComboBoxItem)KindBox.SelectedItem).Tag;
        // Tried with Windows now, so a combination another app has is said here, where it can be changed.
        var refused = _apply(map);
        Map = map;
        if (refused.Count > 0 && (_warnedAbout is null || !_warnedAbout.SameAs(map)))
        {
            _warnedAbout = map;
            Tell($"Another app already uses {string.Join(" and ", refused.Select(t => map.GestureFor(t)))}, so " +
                 $"{(refused.Count == 1 ? "it doesn't" : "they don't")} work from other apps. Choose another letter, or press OK again to keep " +
                 $"{(refused.Count == 1 ? "it" : "them")} anyway.", KeyFor(refused[0]));
            return;
        }
        DialogResult = true;
    }

    private Control FirstDuplicate(HotKeyMap map) =>
        HotKeyMap.Targets.Where(t => map.LetterFor(t) is not null).GroupBy(t => map.LetterFor(t)).FirstOrDefault(g => g.Count() > 1) is { } same
            ? KeyFor(same.Last())
            : CtrlBox;

    /// <summary>Says what's wrong and puts focus where it can be fixed.</summary>
    private void Tell(string message, Control fix)
    {
        SummaryText.Text = message;
        AutomationProperties.SetHelpText(OkButton, message);
        fix.Focus();
        Announcer.Announce(message);
    }
}
