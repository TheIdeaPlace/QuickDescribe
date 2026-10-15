using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32;
using QuickDescribe.Services;

namespace QuickDescribe.Views;

public partial class MainWindow : Window
{
    /// <summary>How long to wait after minimizing before taking the screen, so the window behind has become the active one.</summary>
    private static readonly TimeSpan GetOutOfTheWay = TimeSpan.FromMilliseconds(350);

    private readonly Settings _settings = Settings.Load();
    private readonly WindowsAI _ai = new();
    private readonly Requests _requests = new();

    /// <summary>Given up when Quick Describe closes, so getting the model ready doesn't outlive it.</summary>
    private readonly CancellationTokenSource _closing = new();

    private HotKeys? _hotKeys;
    private IntPtr _handle;

    /// <summary>The window that was active when a hotkey was pressed: Escape goes back to it.</summary>
    private IntPtr _returnTo;

    /// <summary>What's being described, for saying it stopped.</summary>
    private string? _describing;

    /// <summary>A dialog (Open Picture) is open: hotkeys wait until it's closed.</summary>
    private bool _dialogOpen;

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
        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            _closing.Cancel();
            _requests.Cancel();
            _hotKeys?.Dispose();
            _ai.Dispose();
        };
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        // Never in its own pictures: a screen or window taken while Quick Describe is still showing would otherwise read
        // its last result back as part of the screen.
        Native.SetWindowDisplayAffinity(_handle, Native.WDA_EXCLUDEFROMCAPTURE);
        _hotKeys = new HotKeys(HwndSource.FromHwnd(_handle));
        _hotKeys.Pressed += OnHotKey;
        if (_hotKeys.Unavailable.Count > 0)
            ResultBox.Text = HotKeyConflict() + Environment.NewLine + Environment.NewLine + Welcome();
        _ = GetModelReadyAsync(_closing.Token);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ResultBox.Focus();
        // Said as well as shown: a screen reader reads only the box's first line when it gets focus.
        if (_hotKeys is { Unavailable.Count: > 0 }) Announcer.Announce(HotKeyConflict());
    }

    private string HotKeyConflict() =>
        $"Another app already uses {string.Join(", ", _hotKeys!.Unavailable.Select(HotKeys.GestureFor))}, so " +
        "those don't work from other apps. The Describe menu still does the same.";

    /// <summary>Gets Windows' model ready in the background if it only needs downloading, so the first description isn't
    /// the one that waits for it.</summary>
    private static async Task GetModelReadyAsync(CancellationToken cancel)
    {
        try
        {
            if ((await WindowsAI.CheckAsync(cancel)).State == "NotReady") await WindowsAI.PrepareAsync(cancel);
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
        if (_dialogOpen)
        {
            // The dialog has focus, and describing would bring this window up behind it.
            SystemSounds.Beep.Play();
            return;
        }
        var foreground = Native.GetForegroundWindow();
        // Pressed in Quick Describe itself: describe what's behind it, as the Describe menu does.
        if (foreground == _handle && target != Target.Clipboard)
        {
            _ = DescribeBehindAsync(target);
            return;
        }
        _returnTo = foreground == _handle ? IntPtr.Zero : foreground;
        _ = DescribeAsync(target, foreground);
    }

    /// <summary>The Describe menu, or a hotkey pressed here: gets out of the way first, so the window behind is the active
    /// one, then comes back.</summary>
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
        var request = _requests.Start();
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
        catch (Exception ex)
        {
            // Anything at all: said, rather than lost with Quick Describe still minimized.
            (picture, problem) = (null, $"The picture couldn't be taken: {ex.Message.Trim()}");
        }
        if (!_requests.IsCurrent(request)) return;
        BringToFront();
        if (picture is null)
        {
            _requests.Finish(request);
            ShowMessage(problem ?? "Nothing to describe.", null);
        }
        else
        {
            await DescribeAsync(picture, request);
        }
    }

    private Task DescribeAsync(Picture picture) => DescribeAsync(picture, _requests.Start());

    private async Task DescribeAsync(Picture picture, Request request)
    {
        _describing = picture.Source;
        Title = $"Describing {picture.Source} - {AppInfo.Name}";
        ResultBox.Text = $"Describing {picture.Source}…";
        Announcer.Announce($"Describing {picture.Source}");
        Result result;
        try
        {
            result = await Describing.RunAsync(picture.Image, _settings.Kind, _ai, request.Token);
        }
        catch (OperationCanceledException)
        {
            return; // given up, by Escape or a newer request
        }
        catch (Exception ex)
        {
            if (!_requests.IsCurrent(request)) return;
            _requests.Finish(request);
            ShowMessage($"{picture.Source} couldn't be described: {ex.Message.Trim()}", picture.Source);
            return;
        }
        if (!_requests.IsCurrent(request)) return;
        _requests.Finish(request);
        _describing = null;
        Title = $"{picture.Source} - {AppInfo.Name}";
        ResultBox.Text = result.BoxText();
        ResultBox.CaretIndex = 0;
        ResultBox.ScrollToHome();
        // Spoken and focused only while Quick Describe is in front. Switched away from meanwhile, it doesn't take focus
        // back or speak over another app: the result is here when you return.
        if (IsActive)
        {
            ResultBox.Focus();
            Announcer.Announce(result.Announcement());
        }
    }

    /// <summary>Shows and speaks a message in place of a result. It doesn't stop a description on its way: that replaces
    /// the message when it arrives.</summary>
    private void ShowMessage(string message, string? source)
    {
        if (!_requests.IsRunning) Title = source is null ? AppInfo.Name : $"{source} - {AppInfo.Name}";
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
        bool? chosen;
        _dialogOpen = true;
        try
        {
            chosen = dialog.ShowDialog(this);
        }
        finally
        {
            _dialogOpen = false;
        }
        if (chosen == true) DescribeFile(dialog.FileName);
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
        "Escape stops describing, minimizes Quick Describe and goes back to where you were. The hotkeys keep working." + Environment.NewLine +
        "Alt+F4 quits, and the hotkeys stop.";

    private async void OnCheckAI(object sender, RoutedEventArgs e)
    {
        try
        {
            ShowMessage("Checking Windows AI…", null);
            var details = await WindowsAI.DetailsAsync(_closing.Token);
            var lines = new List<string>();
            if (details.Usable)
            {
                lines.Add(details.State == "Ready"
                    ? "Windows AI is ready to describe pictures."
                    : "Windows AI can describe pictures once its model is ready. Getting it ready now; the first time, this can take a few minutes.");
                if (details.HelperVersion is { } v) lines.Add($"{AppInfo.HelperName} {v}.");
                if (details.Components.TryGetValue("textRecognition", out var recognizer))
                    lines.Add(recognizer == "Ready"
                        ? "Windows AI's text recognizer is ready, and reads the text in pictures."
                        : $"Windows AI's text recognizer: {recognizer}. Windows' built-in text recognition reads the text instead.");
                if (details.Components.TryGetValue("languageModel", out var language))
                    lines.Add($"Phi Silica, Windows' language model, which answering questions would need: {language}.");
            }
            else
            {
                lines.Add(details.Message ?? $"Windows AI isn't available ({details.State}).");
            }
            lines.Add(TextLanguages());
            lines.Add("Windows' AI declines to describe most screens of text. The text is read instead.");
            ShowMessage(string.Join(Environment.NewLine, lines), null);
            if (details.State == "NotReady") await GetModelReadyAsync(_closing.Token);
        }
        catch (OperationCanceledException)
        {
            // Closing.
        }
    }

    private static string TextLanguages()
    {
        var languages = Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages.Select(l => l.DisplayName).ToList();
        return languages.Count == 0 ? TextRecognition.NoLanguageMessage : $"Windows' built-in text recognition reads {string.Join(", ", languages)}.";
    }

    private void OnAbout(object sender, RoutedEventArgs e)
    {
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "";
        ShowMessage($"{AppInfo.Name} {version}, by Kelly Ford. Describes the screen and pictures with Windows' on-device AI and text recognition. " +
                    $"Source code: https://github.com/{AppInfo.Repository}", null);
    }

    private void BuildKindMenu()
    {
        // Access keys, so a letter goes straight to a type in the menu.
        var headers = new Dictionary<string, string>
        {
            ["accessible"] = "_Accessible", ["detailed"] = "_Detailed", ["brief"] = "_Brief", ["diagram"] = "Dia_gram",
        };
        foreach (var (id, title) in WindowsAI.Kinds)
        {
            var item = new MenuItem { Header = headers.GetValueOrDefault(id, title), IsCheckable = true, IsChecked = id == _settings.Kind, Tag = id };
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

    /// <summary>Escape: stops describing, gets out of the way, and goes back to the window that was active when the hotkey
    /// was pressed.</summary>
    private void GoBack()
    {
        if (_requests.Cancel())
        {
            ResultBox.Text = $"Stopped describing {_describing ?? "the picture"}.";
            Title = AppInfo.Name;
        }
        _describing = null;
        var returnTo = _returnTo;
        _returnTo = IntPtr.Zero;
        WindowState = WindowState.Minimized;
        if (returnTo != IntPtr.Zero && Native.IsWindow(returnTo)) Native.SetForegroundWindow(returnTo);
    }
}
