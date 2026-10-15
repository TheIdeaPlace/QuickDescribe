using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32;
using QuickDescribe.Services;

namespace QuickDescribe.Views;

public partial class MainWindow : Window
{
    /// <summary>How long to wait after minimizing before taking the screen, so Quick Describe is gone from it and the
    /// window behind has become active.</summary>
    private static readonly TimeSpan GetOutOfTheWay = TimeSpan.FromMilliseconds(350);

    private readonly Settings _settings = Settings.Load();
    private readonly WindowsAI _ai = new();
    private HotKeys? _hotKeys;
    private IntPtr _handle;

    /// <summary>The window that was active when a hotkey was pressed: Escape goes back to it.</summary>
    private IntPtr _returnTo;

    /// <summary>Counts requests, so only the latest one's result is shown when several overlap.</summary>
    private int _request;

    public MainWindow()
    {
        InitializeComponent();
        BuildKindMenu();
        ScreenItem.InputGestureText = HotKeys.GestureFor(Target.Screen);
        WindowItem.InputGestureText = HotKeys.GestureFor(Target.Window);
        FocusItem.InputGestureText = HotKeys.GestureFor(Target.Focus);
        ClipboardItem.InputGestureText = $"Ctrl+V, or {HotKeys.GestureFor(Target.Clipboard)}";
        ResultBox.Text = Welcome();
        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => ResultBox.Focus();
        Closed += (_, _) =>
        {
            _hotKeys?.Dispose();
            _ai.Dispose();
        };
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        _hotKeys = new HotKeys(HwndSource.FromHwnd(_handle));
        _hotKeys.Pressed += OnHotKey;
        if (_hotKeys.Unavailable.Count > 0)
            ResultBox.Text = $"Another app already uses {string.Join(", ", _hotKeys.Unavailable.Select(HotKeys.GestureFor))}, so " +
                             "those don't work from other apps. The Describe menu still does the same." +
                             Environment.NewLine + Environment.NewLine + Welcome();
        _ = GetModelReadyAsync();
    }

    /// <summary>Gets Windows' model ready in the background if it only needs downloading, so the first description isn't
    /// the one that waits for it.</summary>
    private static async Task GetModelReadyAsync()
    {
        try
        {
            if ((await WindowsAI.CheckAsync()).State == "NotReady") await WindowsAI.PrepareAsync();
        }
        catch (Exception)
        {
            // Describing says what's wrong when it's asked for.
        }
    }

    private static string Welcome() =>
        $"From any app, press {HotKeys.Modifiers} with:" + Environment.NewLine +
        "S to describe the screen," + Environment.NewLine +
        "W to describe the active window," + Environment.NewLine +
        "F to describe the item with focus," + Environment.NewLine +
        "V to describe a picture on the clipboard." + Environment.NewLine + Environment.NewLine +
        "Quick Describe comes to the front with the result here. Escape goes back to where you were. " +
        "You can also open a picture file (Ctrl+O) or drop one on this window. " +
        "Pictures are described with Windows' on-device AI on Copilot+ PCs, and their text read with Windows' text recognition. " +
        "Nothing leaves this PC.";

    // ---- Describing ----

    private void OnHotKey(Target target)
    {
        var foreground = Native.GetForegroundWindow();
        // Pressed in Quick Describe itself: describe what's behind it, as the Describe menu does.
        if (foreground == _handle && target != Target.Clipboard)
        {
            _ = DescribeBehindAsync(target);
            return;
        }
        _returnTo = target == Target.Clipboard ? IntPtr.Zero : foreground;
        _ = DescribeAsync(target, foreground);
    }

    /// <summary>The Describe menu, or a hotkey pressed here: gets out of the way first, so the screen shows the window
    /// behind and that window is the active one, then comes back.</summary>
    private async Task DescribeBehindAsync(Target target)
    {
        WindowState = WindowState.Minimized;
        await Task.Delay(GetOutOfTheWay);
        var foreground = Native.GetForegroundWindow();
        _returnTo = foreground == _handle ? IntPtr.Zero : foreground;
        await DescribeAsync(target, foreground);
    }

    private async Task DescribeAsync(Target target, IntPtr foreground)
    {
        Picture? picture;
        string? problem;
        try
        {
            (picture, problem) = target switch
            {
                Target.Screen => (ScreenCapture.Screen(foreground), null),
                Target.Window => ScreenCapture.Window(foreground),
                Target.Focus => await ScreenCapture.FocusAsync(foreground),
                _ => (Pictures.FromClipboard(), null),
            };
        }
        catch (PictureException ex)
        {
            (picture, problem) = (null, ex.Message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            (picture, problem) = (null, $"The picture couldn't be taken: {ex.Message.Trim()}");
        }
        BringToFront();
        if (picture is null) ShowMessage(problem ?? "Nothing to describe.", null);
        else await DescribeAsync(picture);
    }

    private async Task DescribeAsync(Picture picture)
    {
        var request = ++_request;
        Title = $"Describing {picture.Source} - {AppInfo.Name}";
        ResultBox.Text = $"Describing {picture.Source}…";
        Announcer.Announce($"Describing {picture.Source}");
        Result result;
        try
        {
            result = await Describing.RunAsync(picture.Image, _settings.Kind, _ai);
        }
        catch (Exception ex)
        {
            if (request == _request) ShowMessage($"{picture.Source} couldn't be described: {ex.Message.Trim()}", picture.Source);
            return;
        }
        if (request != _request) return; // a newer request has taken over
        Title = $"{picture.Source} - {AppInfo.Name}";
        ResultBox.Text = result.BoxText();
        ResultBox.CaretIndex = 0;
        ResultBox.ScrollToHome();
        ResultBox.Focus();
        Announcer.Announce(result.Announcement());
    }

    /// <summary>Shows and speaks a message in place of a result.</summary>
    private void ShowMessage(string message, string? source)
    {
        ++_request;
        Title = source is null ? AppInfo.Name : $"{source} - {AppInfo.Name}";
        ResultBox.Text = message;
        ResultBox.CaretIndex = 0;
        ResultBox.Focus();
        Announcer.Announce(message);
    }

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
        // Windows lets an app that just got a hotkey come to the front; if it still didn't, ask once more directly.
        if (!IsActive) Native.SetForegroundWindow(_handle);
        ResultBox.Focus();
    }

    // ---- Commands ----

    private void OnScreen(object sender, RoutedEventArgs e) => _ = DescribeBehindAsync(Target.Screen);
    private void OnWindow(object sender, RoutedEventArgs e) => _ = DescribeBehindAsync(Target.Window);
    private void OnFocus(object sender, RoutedEventArgs e) => _ = DescribeBehindAsync(Target.Focus);

    private void OnClipboard(object sender, RoutedEventArgs e)
    {
        _returnTo = IntPtr.Zero;
        _ = DescribeAsync(Target.Clipboard, IntPtr.Zero);
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Open Picture", Filter = Pictures.OpenFilter };
        if (dialog.ShowDialog(this) == true) DescribeFile(dialog.FileName);
    }

    private void DescribeFile(string path)
    {
        _returnTo = IntPtr.Zero;
        try
        {
            _ = DescribeAsync(Pictures.FromFile(path));
        }
        catch (PictureException ex)
        {
            ShowMessage(ex.Message, null);
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetData(DataFormats.FileDrop) is string[] files && Pictures.FirstPictureFile(files) is not null
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            if (Pictures.FirstPictureFile(files) is { } file) DescribeFile(file);
            else ShowMessage("That isn't a picture file.", null);
        }
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(ResultBox.Text);
            Announcer.Announce("Copied");
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            Announcer.Announce("The clipboard is busy. Try again.");
        }
    }

    private void OnExit(object sender, RoutedEventArgs e) => Close();

    private void OnShortcuts(object sender, RoutedEventArgs e) => ShowMessage(Welcome() + Environment.NewLine + Environment.NewLine + InAppKeys(), null);

    private static string InAppKeys() =>
        "In Quick Describe:" + Environment.NewLine +
        "Ctrl+O opens a picture file." + Environment.NewLine +
        "Ctrl+V describes the picture on the clipboard." + Environment.NewLine +
        "Ctrl+Shift+C copies the result." + Environment.NewLine +
        "Escape minimizes Quick Describe and goes back to where you were. The hotkeys keep working." + Environment.NewLine +
        "Alt+F4 quits, and the hotkeys stop.";

    private async void OnCheckAI(object sender, RoutedEventArgs e)
    {
        ShowMessage("Checking Windows AI…", null);
        var details = await WindowsAI.DetailsAsync();
        var lines = new List<string>();
        if (details.Usable)
        {
            lines.Add(details.State == "Ready"
                ? "Windows AI is ready to describe pictures."
                : "Windows AI can describe pictures once its model is ready. Getting it ready now; the first time, this can take a few minutes.");
            if (details.HelperVersion is { } v) lines.Add($"{AppInfo.HelperName} {v}.");
            if (details.Components.TryGetValue("languageModel", out var language))
                lines.Add($"Phi Silica, Windows' language model, which answering questions would need: {language}.");
        }
        else
        {
            lines.Add(details.Message ?? $"Windows AI isn't available ({details.State}).");
        }
        lines.Add(TextLanguages());
        ShowMessage(string.Join(Environment.NewLine, lines), null);
        if (details.State == "NotReady") await GetModelReadyAsync();
    }

    private static string TextLanguages()
    {
        var languages = Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages.Select(l => l.DisplayName).ToList();
        return languages.Count == 0 ? TextRecognition.NoLanguageMessage : $"Text recognition reads {string.Join(", ", languages)}.";
    }

    private void OnAbout(object sender, RoutedEventArgs e)
    {
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "";
        ShowMessage($"{AppInfo.Name} {version}, by Kelly Ford. Describes the screen and pictures with Windows' on-device AI and text recognition. " +
             $"Source code: https://github.com/{AppInfo.Repository}", null);
    }

    private void BuildKindMenu()
    {
        foreach (var (id, title) in WindowsAI.Kinds)
        {
            var item = new MenuItem { Header = title, IsCheckable = true, IsChecked = id == _settings.Kind, Tag = id };
            item.Click += (_, _) =>
            {
                _settings.Kind = id;
                _settings.Save();
                foreach (var other in KindMenu.Items.OfType<MenuItem>()) other.IsChecked = (string)other.Tag == id;
            };
            KindMenu.Items.Add(item);
        }
    }

    // ---- Keys ----

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        if (e.Key == Key.Escape && modifiers == ModifierKeys.None && !IsMenuOpen())
        {
            e.Handled = true;
            GoBack();
        }
        else if (e.Key == Key.O && modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            OnOpen(sender, e);
        }
        else if (e.Key == Key.V && modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            OnClipboard(sender, e);
        }
        else if (e.Key == Key.C && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            e.Handled = true;
            OnCopy(sender, e);
        }
        else if (e.Key == Key.F1 && modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            OnShortcuts(sender, e);
        }
    }

    private static bool IsMenuOpen() => Keyboard.FocusedElement is MenuItem;

    /// <summary>Escape: out of the way, and back to the window that was active when the hotkey was pressed.</summary>
    private void GoBack()
    {
        var returnTo = _returnTo;
        _returnTo = IntPtr.Zero;
        WindowState = WindowState.Minimized;
        if (returnTo != IntPtr.Zero && Native.IsWindow(returnTo)) Native.SetForegroundWindow(returnTo);
    }
}
