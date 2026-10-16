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

    /// <summary>A dialog is open, or about to be (Report a Bug while it's prepared): hotkeys wait until it's closed. Any
    /// modal dialog also disables this window, which <see cref="OnHotKey"/> checks too.</summary>
    private bool _dialogOpen;

    /// <summary>A result that arrived while Quick Describe wasn't in front: spoken when it next is.</summary>
    private string? _unspoken;

    /// <summary>How long Windows takes to show the screen without Quick Describe once it's asked to leave it out of
    /// captures: a frame or two.</summary>
    private static readonly TimeSpan LeaveOutOfCapture = TimeSpan.FromMilliseconds(100);

    public MainWindow()
    {
        InitializeComponent();
        BuildKindMenu();
        ShowGestures(_settings.HotKeys);
        ResultBox.Text = Welcome();
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Activated += (_, _) =>
        {
            if (_unspoken is not { } text) return;
            _unspoken = null;
            ResultBox.Focus();
            Announcer.Announce(text);
        };
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
        UseHotKeys(_settings.HotKeys);
        if (_hotKeys!.Unavailable.Count > 0)
            ResultBox.Text = HotKeyConflict() + Environment.NewLine + Environment.NewLine + Welcome();
        _ = GetModelReadyAsync(_closing.Token);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ResultBox.Focus();
        // Said as well as shown: a screen reader reads only the box's first line when it gets focus.
        if (_hotKeys is { Unavailable.Count: > 0 })
        {
            Log.Write("start", $"Hotkeys another app has: {string.Join(", ", _hotKeys.Unavailable.Select(_hotKeys.Map.GestureFor))}");
            Announcer.Announce(HotKeyConflict());
        }
        AfterStart();
    }

    private string HotKeyConflict() =>
        $"Another app already uses {string.Join(", ", _hotKeys!.Unavailable.Select(_hotKeys.Map.GestureFor))}, so " +
        "those don't work from other apps. The Describe menu still does the same.";

    /// <summary>Gets Windows' model ready in the background if it only needs downloading, then warms it up, so the first
    /// description isn't the one that waits for either. Not on energy saver, where the person has asked Windows to save
    /// power; the first description then just takes longer.</summary>
    private async Task GetModelReadyAsync(CancellationToken cancel)
    {
        try
        {
            var readiness = await WindowsAI.CheckAsync(cancel);
            if (readiness.State == "NotReady") readiness = await WindowsAI.PrepareAsync(cancel);
            if (readiness.State != "Ready" || EnergySaverOn()) return;
            var took = await _ai.WarmUpAsync(cancel);
            Log.Write("helper", $"Warmed up in {took.TotalSeconds:0.0} s");
        }
        catch (OperationCanceledException)
        {
            // Closing.
        }
        catch (Exception ex)
        {
            // Describing says what's wrong when it's asked for.
            Log.Write("helper", $"Warming up didn't work: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool EnergySaverOn()
    {
        try
        {
            return Windows.System.Power.PowerManager.EnergySaverStatus == Windows.System.Power.EnergySaverStatus.On;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private string Welcome() => Welcome(_hotKeys?.Map ?? _settings.HotKeys);

    /// <summary>What the Result box says at first, and F1: the hotkeys as they're set, and what else there is.</summary>
    internal static string Welcome(HotKeyMap map)
    {
        var what = new Dictionary<Target, string>
        {
            [Target.Screen] = "describe the screen",
            [Target.Window] = "describe the active window",
            [Target.Focus] = "describe the item with focus",
            [Target.Clipboard] = "describe a picture on the clipboard",
        };
        var on = HotKeyMap.Targets.Where(t => map.LetterFor(t) is not null).ToList();
        var keys = on.Count == 0
            ? "No hotkeys are set: the Describe menu does the same. File, Settings sets them."
            : $"From any app, press {map.ModifierText} with:" + Environment.NewLine +
              string.Join("," + Environment.NewLine, on.Select(t => $"{map.LetterFor(t)} to {what[t]}")) + "." +
              (on.Count < HotKeyMap.Targets.Length ? Environment.NewLine + "File, Settings sets the others." : "");
        return keys + Environment.NewLine + Environment.NewLine +
        "Quick Describe comes to the front with the result here. Escape goes back to where you were. " +
        "Describe, Description Type chooses Accessible, Detailed, Brief or Diagram descriptions. " +
        "You can also open a picture file (Ctrl+O) or drop one on this window. " +
        "Pictures are described with Windows' on-device AI on Copilot+ PCs, and their text read with Windows' text recognition. " +
        "File, Settings chooses the hotkeys. Pictures never leave this PC.";
    }

    // ---- Describing ----

    private void OnHotKey(Target target)
    {
        if (_dialogOpen || !Native.IsWindowEnabled(_handle))
        {
            // A dialog has focus (Open Picture, Report a Bug, a question or a message), and describing would bring this
            // window up behind it.
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
        // Quick Describe still showing (switched away from without Escape) is left out of its own picture, which would
        // otherwise read its last result back as part of the screen. Only for the moment of taking it: left out all the
        // time, it would be missing from screen sharing and other people's screenshots too.
        var hide = target != Target.Clipboard && IsVisible && WindowState != WindowState.Minimized &&
                   Native.SetWindowDisplayAffinity(_handle, Native.WDA_EXCLUDEFROMCAPTURE);
        try
        {
            if (hide) await Task.Delay(LeaveOutOfCapture);
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
            Log.Error("capture", $"Taking the picture ({target}) failed", ex);
            (picture, problem) = (null, $"The picture couldn't be taken: {ex.Message.Trim()}");
        }
        finally
        {
            if (hide) Native.SetWindowDisplayAffinity(_handle, Native.WDA_NONE);
        }
        if (!_requests.IsCurrent(request)) return;
        BringToFront();
        if (picture is null)
        {
            _requests.Finish(request);
            _describing = null;
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
        _unspoken = null;
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
            _describing = null;
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
        // Spoken and focused only while Quick Describe is in front. Switched away from meanwhile (or never brought to the
        // front), it doesn't take focus or speak over another app: it's spoken when you return.
        if (IsActive)
        {
            ResultBox.Focus();
            Announcer.Announce(result.Announcement());
        }
        else
        {
            _unspoken = result.Announcement();
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

    // ---- Settings ----

    /// <summary>Registers a map's hotkeys in place of the ones in use, and shows them in the menus. Returns the ones another
    /// app already has.</summary>
    private IReadOnlyList<Target> UseHotKeys(HotKeyMap map)
    {
        _hotKeys?.Dispose();
        _hotKeys = new HotKeys(HwndSource.FromHwnd(_handle), map);
        _hotKeys.Pressed += OnHotKey;
        ShowGestures(map);
        return _hotKeys.Unavailable;
    }

    private void ShowGestures(HotKeyMap map)
    {
        ScreenItem.InputGestureText = map.GestureFor(Target.Screen) ?? "";
        WindowItem.InputGestureText = map.GestureFor(Target.Window) ?? "";
        FocusItem.InputGestureText = map.GestureFor(Target.Focus) ?? "";
        ClipboardItem.InputGestureText = map.GestureFor(Target.Clipboard) is { } clipboard ? $"Ctrl+V, or {clipboard}" : "Ctrl+V";
    }

    private void ShowKind()
    {
        foreach (var item in KindMenu.Items.OfType<MenuItem>()) item.IsChecked = (string)item.Tag == _settings.Kind;
    }

    /// <summary>File > Settings: the hotkeys and the description type. The hotkeys chosen are tried as OK is pressed, so a
    /// combination another app has can be changed there; Cancel puts back the ones from before.</summary>
    private void OnSettings(object sender, RoutedEventArgs e)
    {
        if (_dialogOpen) return;
        var before = _hotKeys!.Map;
        var dialog = new SettingsWindow(before, _settings.Kind, UseHotKeys) { Owner = this };
        _dialogOpen = true;
        bool saved;
        try
        {
            saved = dialog.ShowDialog() == true;
        }
        finally
        {
            _dialogOpen = false;
        }
        if (!saved)
        {
            // OK may have tried some first (another app had one, and the person then cancelled).
            if (!_hotKeys.Map.SameAs(before)) UseHotKeys(before);
            ResultBox.Focus();
            return;
        }
        _settings.HotKeys = dialog.Map;
        _settings.Kind = dialog.Kind;
        _settings.Save();
        ShowKind();
        Log.Write("settings", $"Hotkeys {SettingsWindow.Summary(dialog.Map)} Description type {dialog.Kind}.");
        // The welcome or F1 text says the hotkeys, so it's brought up to date; a result in the box is left alone.
        if (!_requests.IsRunning && ResultBox.Text.Contains(Environment.NewLine + "Quick Describe comes to the front", StringComparison.Ordinal))
            ResultBox.Text = Welcome();
        ResultBox.Focus();
        Announcer.Announce("Settings saved. " + SettingsWindow.Summary(dialog.Map));
    }

    private void OnShortcuts(object sender, RoutedEventArgs e) => ShowMessage(Welcome() + Environment.NewLine + Environment.NewLine + InAppKeys(), null);

    private static string InAppKeys() =>
        "In Quick Describe:" + Environment.NewLine +
        "Ctrl+O opens a picture file." + Environment.NewLine +
        "Ctrl+Comma opens Settings, for the hotkeys and the description type." + Environment.NewLine +
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
                ShowKind();
                Announcer.Announce($"{title} descriptions");
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
        else if (e.Key == Key.OemComma && modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            OnSettings(sender, e);
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
        _unspoken = null;
        var returnTo = _returnTo;
        _returnTo = IntPtr.Zero;
        WindowState = WindowState.Minimized;
        if (returnTo != IntPtr.Zero && Native.IsWindow(returnTo)) Native.SetForegroundWindow(returnTo);
    }
}
