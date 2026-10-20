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

    /// <summary>Windows' startup task, for Settings' "Start Quick Describe when I sign in to Windows".</summary>
    private readonly IStartAtSignIn _startup = new StartAtSignIn();

    /// <summary>File, Exit (or the notification area icon's Exit, an update, Windows signing out): closing quits, even
    /// with Keep running on.</summary>
    private bool _exiting;

    /// <summary>Shown while the window is closed and Quick Describe keeps running.</summary>
    private TrayIcon? _tray;

    /// <summary>Windows started this run at sign-in: nothing is said or asked until the person first comes to the window
    /// with nothing being described (<see cref="GreetIfQuiet"/>).</summary>
    private bool _quiet;

    public MainWindow()
    {
        InitializeComponent();
        BuildKindMenu();
        ShowGestures(_settings.HotKeys);
        ShowExitGesture();
        ResultBox.Text = Welcome();
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Activated += (_, _) =>
        {
            // Set off for a quiet start, so it didn't take focus; from now on, showing the window comes to the front.
            ShowActivated = true;
            if (GreetIfQuiet()) return;
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
            _tray?.Dispose();
        };
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        UseHotKeys(_settings.HotKeys);
        ShowTrayWhileKeptRunning();
        if (_hotKeys!.Unavailable.Count > 0)
        {
            Log.Write("start", $"Hotkeys another app has: {string.Join(", ", _hotKeys.Unavailable.Select(_hotKeys.Map.GestureFor))}");
            ResultBox.Text = HotKeyConflict() + Environment.NewLine + Environment.NewLine + Welcome();
        }
        _ = GetModelReadyAsync(_closing.Token);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LookForUpdatesFromNowOn();
        if (_quiet) return;
        ResultBox.Focus();
        // Said as well as shown: a screen reader reads only the box's first line when it gets focus.
        if (_hotKeys is { Unavailable.Count: > 0 }) Announcer.Announce(HotKeyConflict());
        AskAtStart();
    }

    /// <summary>Started by Windows at sign-in: out of the way, so it doesn't take focus from what the person is doing.
    /// In the notification area when Keep running is on (the window isn't shown, but has its handle, so the hotkeys
    /// work), and otherwise minimized on the taskbar.</summary>
    public void StartQuietly()
    {
        _quiet = true;
        Log.Write("start", $"Started by Windows at sign-in, {(_settings.KeepRunning ? "in the notification area" : "minimized")}");
        if (_settings.KeepRunning)
        {
            // The handle, without showing the window: the hotkeys and the notification area icon (OnSourceInitialized).
            new WindowInteropHelper(this).EnsureHandle();
            LookForUpdatesFromNowOn();
            return;
        }
        ShowActivated = false;
        WindowState = WindowState.Minimized;
        Show();
    }

    /// <summary>Started at sign-in, and the person has now come to the window with nothing being described: focus goes to
    /// the Result box, a hotkey another app has is said, and what a start asks is asked. True when it was.</summary>
    private bool GreetIfQuiet()
    {
        if (!_quiet || !IsActive || _requests.IsRunning) return false;
        _quiet = false;
        ResultBox.Focus();
        if (_hotKeys is { Unavailable.Count: > 0 }) Announcer.Announce(HotKeyConflict());
        AskAtStart();
        return true;
    }

    // ---- Closing, and keeping running ----

    /// <summary>Whether closing hides the window rather than quitting.</summary>
    internal static bool HidesOnClose(bool keepRunning, bool exiting) => keepRunning && !exiting;

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (HidesOnClose(_settings.KeepRunning, _exiting))
        {
            e.Cancel = true;
            // After the close is over: the note's message box, inside Closing, would let a second launch's show signal
            // call Show on a window that's closing, which WPF refuses.
            Dispatcher.BeginInvoke(CloseToTray);
            return;
        }
        Log.Write("exit", "Quitting");
        base.OnClosing(e);
    }

    /// <summary>Closing with Keep running on: stops a description and hides the window; the notification area icon is
    /// already there. The first time, says where Quick Describe went and how to quit.</summary>
    private void CloseToTray()
    {
        if (!IsVisible) return;
        if (_requests.Cancel()) ResultBox.Text = $"Stopped describing {_describing ?? "the picture"}.";
        _describing = null;
        _unspoken = null;
        _returnTo = IntPtr.Zero;
        Title = AppInfo.Name;
        if (!_settings.KeepRunningSaid)
        {
            _settings.KeepRunningSaid = true;
            _settings.Save();
            // A message box, which screen readers read as it opens: an announcement from a window about to hide isn't.
            MessageBox.Show(this, KeepRunningText, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        ShowTrayWhileKeptRunning();
        Hide();
        Log.Write("exit", "Closed to the notification area");
    }

    internal const string KeepRunningText =
        "Quick Describe is still running, in the notification area, so the hotkeys keep working. " +
        "To quit, choose File, Exit, or Exit from its icon's menu in the notification area. Settings can turn this off.";

    /// <summary>The notification area icon is there the whole time Keep running is on, so its Exit is always there to
    /// quit with, whether the window is showing, minimized or closed.</summary>
    private void ShowTrayWhileKeptRunning()
    {
        if (_settings.KeepRunning)
        {
            _tray ??= new TrayIcon(BringToFront, Exit);
            _tray.Show();
        }
        else _tray?.Hide();
    }

    /// <summary>Quits, whatever Keep running says.</summary>
    public void Exit()
    {
        _exiting = true;
        Close();
    }

    /// <summary>Windows is signing out, or an update closes Quick Describe: closing then quits.</summary>
    public void AllowClose() => _exiting = true;

    /// <summary>Alt+F4 next to Exit only while it quits.</summary>
    private void ShowExitGesture() => ExitItem.InputGestureText = _settings.KeepRunning ? "" : "Alt+F4";

    private string HotKeyConflict() =>
        $"{_hotKeys!.Map.Refused(_hotKeys.Unavailable)} The Describe menu still does the same, and File, Settings chooses others.";

    /// <summary>Gets Windows' models ready in the background if they only need downloading, then warms them up, so the first
    /// description isn't the one that waits for either: a download takes longer than a request may. No warming up on energy
    /// saver, where the person has asked Windows to save power; the first description then just takes longer.</summary>
    private async Task GetModelReadyAsync(CancellationToken cancel)
    {
        try
        {
            var readiness = await WindowsAI.CheckAsync(cancel);
            if (readiness.State == "NotReady") readiness = await WindowsAI.PrepareAsync(cancel);
            if (WindowsAI.RecognizerNotReady(readiness))
            {
                readiness = await WindowsAI.PrepareTextRecognitionAsync(cancel);
                Log.Write("helper", $"Getting the text recognizer ready: {readiness.Components.GetValueOrDefault("textRecognition", "unknown")}");
            }
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

    private string Welcome() => Welcome(_hotKeys?.Map ?? _settings.HotKeys, WindowsAI.KnownTextOnly);

    /// <summary>What the Result box says at first, and F1: the hotkeys as they're set, and what else there is.
    /// <paramref name="textOnly"/>: this PC is known to read text only.</summary>
    internal static string Welcome(HotKeyMap map, bool textOnly = false)
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
        (textOnly ? "" : "Describe, Description Type chooses Accessible, Detailed, Brief or Diagram descriptions. ") +
        "You can also open a picture file (Ctrl+O) or drop one on this window. " +
        (textOnly
            ? "This PC reads the text in pictures with Windows' text recognition. Describing them needs a Copilot+ PC. "
            : "Pictures are described with Windows' on-device AI on Copilot+ PCs, and their text read with Windows' text recognition. ") +
        "File, Settings chooses the hotkeys, and whether Quick Describe starts with Windows. Pictures never leave this PC.";
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
        var sayTextOnly = result.TextOnly && !_settings.TextOnlySaid;
        if (sayTextOnly)
        {
            _settings.TextOnlySaid = true;
            _settings.Save();
        }
        ResultBox.Text = result.BoxText(sayTextOnly);
        ResultBox.CaretIndex = 0;
        ResultBox.ScrollToHome();
        // Spoken and focused only while Quick Describe is in front. Switched away from meanwhile (or never brought to the
        // front), it doesn't take focus or speak over another app: it's spoken when you return.
        if (IsActive)
        {
            ResultBox.Focus();
            Announcer.Announce(result.Announcement(sayTextOnly));
        }
        else
        {
            _unspoken = result.Announcement(sayTextOnly);
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

    private void OnExit(object sender, RoutedEventArgs e) => Exit();

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
    private async void OnSettings(object sender, RoutedEventArgs e)
    {
        if (_dialogOpen) return;
        var before = _hotKeys!.Map;
        _dialogOpen = true;
        SettingsWindow dialog;
        bool saved;
        try
        {
            // Asked of Windows each time: Task Manager or Windows Settings can turn it off meanwhile.
            var signIn = await _startup.GetAsync();
            // Quitting, or closed to the notification area, while Windows answered.
            if (_closing.IsCancellationRequested || !IsVisible) return;
            dialog = new SettingsWindow(before, _settings.Kind, UseHotKeys, signIn, _settings.KeepRunning) { Owner = this };
            saved = dialog.ShowDialog() == true;
            if (saved) await SaveSettingsAsync(dialog);
        }
        finally
        {
            _dialogOpen = false;
        }
        if (saved) return;
        // OK may have tried some first (another app had one, and the person then cancelled).
        if (!_hotKeys.Map.SameAs(before)) UseHotKeys(before);
        ResultBox.Focus();
    }

    /// <summary>Keeps what Settings chose, with hotkeys (and dialogs) still held off, so a second Settings can't open while
    /// Windows is changing the startup task.</summary>
    private async Task SaveSettingsAsync(SettingsWindow dialog)
    {
        _settings.HotKeys = dialog.Map;
        _settings.Kind = dialog.Kind;
        _settings.KeepRunning = dialog.KeepRunning;
        _settings.Save();
        ShowKind();
        ShowExitGesture();
        ShowTrayWhileKeptRunning();
        Log.Write("settings", $"Hotkeys {SettingsWindow.Summary(dialog.Map)} Description type {dialog.Kind}. " +
                              $"Start at sign-in {(dialog.SignIn ? "on" : "off")}. Keep running {(dialog.KeepRunning ? "on" : "off")}.");
        // The welcome or F1 text says the hotkeys, so it's brought up to date; a result in the box is left alone.
        if (!_requests.IsRunning && ResultBox.Text.Contains(Environment.NewLine + "Quick Describe comes to the front", StringComparison.Ordinal))
            ResultBox.Text = Welcome();
        ResultBox.Focus();
        Announcer.Announce("Settings saved. " + SettingsWindow.Summary(dialog.Map));
        // Turned on or off as chosen; said when Windows doesn't (turned off in its own startup apps, or by an organization).
        if (await StartAtSignIn.ApplyAsync(_startup, dialog.SignIn) is not { } told) return;
        if (told.OfferStartupApps)
        {
            if (QuestionDialog.AskSafely(this, "Settings", told.Message, "Open Startup Apps opens that page of Windows Settings.", "_Open Startup Apps", "OK"))
                OpenInBrowser("ms-settings:startupapps", "Startup apps are in Windows Settings,");
        }
        else MessageBox.Show(this, told.Message, "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
        ResultBox.Focus();
    }

    private void OnShortcuts(object sender, RoutedEventArgs e) => ShowMessage(Welcome() + Environment.NewLine + Environment.NewLine + InAppKeys(_settings.KeepRunning), null);

    internal static string InAppKeys(bool keepRunning) =>
        "In Quick Describe:" + Environment.NewLine +
        "Ctrl+O opens a picture file." + Environment.NewLine +
        "Ctrl+Comma opens Settings, for the hotkeys, the description type, starting with Windows and keeping running." + Environment.NewLine +
        "Ctrl+V describes the picture on the clipboard." + Environment.NewLine +
        "Ctrl+Shift+C copies the result." + Environment.NewLine +
        "Escape stops describing, minimizes Quick Describe and goes back to where you were. The hotkeys keep working." + Environment.NewLine +
        (keepRunning
            ? "Alt+F4 closes the window, and Quick Describe keeps running in the notification area with the hotkeys working. File, Exit quits."
            : "Alt+F4 quits, and the hotkeys stop.");

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
                    lines.Add(LanguageModelLine(language));
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

    /// <summary>Check Windows AI's line on Phi Silica. Its ready state says only that the model is on the PC: Quick
    /// Describe can't use it yet (Windows answers Access is denied), so "ready" alone would promise what isn't there.</summary>
    internal static string LanguageModelLine(string state) => state == "Ready"
        ? "Phi Silica, Windows' language model, is on this PC. It isn't supported in Quick Describe yet."
        : "Phi Silica, Windows' language model, isn't supported in Quick Describe yet.";

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
        // Started at sign-in and used only with hotkeys, so far: the questions a start asks come now, when the person is
        // done with the result, rather than never. Escape again goes back.
        if (GreetIfQuiet()) return;
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
