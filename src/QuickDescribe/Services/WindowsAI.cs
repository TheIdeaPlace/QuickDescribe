using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace QuickDescribe.Services;

/// <summary>Why a description couldn't be made, with the helper's code (see QuickDescribeAI's Protocol.cs).</summary>
public sealed class DescriptionException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>What the helper said about the model.</summary>
public sealed record AIReadiness(bool Installed, string State, string? Message)
{
    public string? HelperVersion { get; init; }

    /// <summary>The ready state of Windows' other AI features, by name ("textRecognition", "languageModel").</summary>
    public IReadOnlyDictionary<string, string> Components { get; init; } = new Dictionary<string, string>();

    /// <summary>Ready now, or can be made ready (possibly by downloading the model).</summary>
    public bool Usable => Installed && State is "Ready" or "NotReady";
}

/// <summary>
/// Describes pictures with Windows' on-device image description model (Microsoft.Windows.AI.Imaging, on Copilot+ PCs).
/// Windows only lets a packaged app use the model, so the describing happens in QuickDescribeAI, a small packaged helper,
/// and this talks to it in JSON lines: the design of Image Description Toolkit and My Photo Library Described, whose helper
/// this is.
/// </summary>
public sealed class WindowsAI : IDisposable
{
    public const string Alias = "quickdescribe-ai.exe";

    /// <summary>The description kinds, in order: the first is the default.</summary>
    public static readonly (string Id, string Title)[] Kinds =
    [
        ("accessible", "Accessible"),
        ("detailed", "Detailed"),
        ("brief", "Brief"),
        ("diagram", "Diagram"),
    ];

    /// <summary>The longest side sent first, and the sizes tried after an internal error: some pictures that fail at one
    /// size describe fine smaller, and the same size again only fails again (found in IDT).</summary>
    public const uint FirstSize = 2048;
    public static readonly uint[] RetrySizes = [1024, 512];

    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PrepareTimeout = TimeSpan.FromMinutes(15);

    /// <summary>How long an answer about the model is trusted when it isn't final: it may be getting ready, or be turned on
    /// in Settings. Ready, and not supported on this PC, are trusted for the session.</summary>
    private static readonly TimeSpan RecheckAfter = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<IHelperConnection> _start;
    private readonly TimeSpan _timeout;
    private IHelperConnection? _server;
    private int _nextId;

    /// <summary>How many requests Windows didn't answer in time, so a request that waited behind one gives up at once.</summary>
    private int _timeouts;

    /// <summary>Why a request timed out. Windows AI has stopped answering anything on Kelly's PC (17 Oct 2026, after its
    /// AI components updated) while still reporting itself ready, until the PC was restarted.</summary>
    internal const string NotRespondingMessage =
        "Windows' AI didn't answer. If this keeps happening, restarting the PC may help.";

    /// <summary>A description takes about 3 seconds, and the first in a session 6 to 18 (measured 15 Oct 2026), so a
    /// minute and a half is a helper that has stopped answering.</summary>
    public WindowsAI() : this(StartServer, TimeSpan.FromSeconds(90))
    {
    }

    /// <summary>For tests: <paramref name="start"/> gives the helper to talk to, and <paramref name="timeout"/> is how long
    /// an answer may take.</summary>
    internal WindowsAI(Func<IHelperConnection> start, TimeSpan timeout)
    {
        _start = start;
        _timeout = timeout;
    }

    public static string AliasPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", Alias);

    /// <summary>The helper to start. Installed, it's the one in Quick Describe's own package, started directly: a packaged
    /// app's child runs with the package's identity. Otherwise (development), the alias a registered helper adds.</summary>
    public static string HelperPath => PackagedHelper ?? AliasPath;

    private static readonly string? PackagedHelper = FindPackagedHelper();

    private static string? FindPackagedHelper()
    {
        try
        {
            var path = Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, "AI", "QuickDescribeAI.exe");
            return File.Exists(path) ? path : null;
        }
        catch (Exception)
        {
            // No package identity: a development build or a test run.
            return null;
        }
    }

    public static bool IsInstalled => File.Exists(HelperPath);

    public static readonly string NotInstalledMessage =
        $"Describing uses Windows' on-device AI on Copilot+ PCs, through the {AppInfo.HelperName}, which isn't installed on this PC.";

    private static (AIReadiness Readiness, DateTime At)? _checked;

    /// <summary>Whether the model can be used, from the helper's --check, remembered so each picture doesn't start another
    /// helper to ask (see <see cref="RecheckAfter"/>).</summary>
    public static async Task<AIReadiness> CheckAsync(CancellationToken cancel = default)
    {
        if (!IsInstalled) return new AIReadiness(false, "NotInstalled", NotInstalledMessage);
        if (_checked is { } known && (IsFinal(known.Readiness.State) || DateTime.UtcNow - known.At < RecheckAfter)) return known.Readiness;
        AIReadiness readiness;
        try
        {
            readiness = ParseReadiness(await RunOnceAsync("--check", CheckTimeout, cancel));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            readiness = new AIReadiness(false, "Error", $"The {AppInfo.HelperName} didn't start: {ex.Message}");
        }
        _checked = (readiness, DateTime.UtcNow);
        return readiness;
    }

    /// <summary>States that won't change while Quick Describe runs.</summary>
    internal static bool IsFinal(string state) => state is "Ready" or "NotSupportedOnCurrentSystem";

    /// <summary>The helper's --details: its readiness plus the other Windows AI features' states, for Check Windows AI.</summary>
    public static async Task<AIReadiness> DetailsAsync(CancellationToken cancel = default)
    {
        if (!IsInstalled) return new AIReadiness(false, "NotInstalled", NotInstalledMessage);
        try
        {
            return ParseReadiness(await RunOnceAsync("--details", CheckTimeout, cancel));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new AIReadiness(false, "Error", $"The {AppInfo.HelperName} didn't start: {ex.Message}");
        }
    }

    /// <summary>Gets the model ready, which the first time can mean a download of several minutes.</summary>
    public static async Task<AIReadiness> PrepareAsync(CancellationToken cancel = default)
    {
        var readiness = ParseReadiness(await RunOnceAsync("--prepare", PrepareTimeout, cancel));
        _checked = (readiness, DateTime.UtcNow);
        return readiness;
    }

    internal static AIReadiness ParseReadiness(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return new AIReadiness(false, "Error", $"The {AppInfo.HelperName} didn't answer.");
        try
        {
            var o = JsonNode.Parse(line) as JsonObject;
            var identity = o?["identity"]?.GetValue<bool>() ?? false;
            var state = o?["state"]?.GetValue<string>() ?? "Error";
            var message = o?["message"]?.GetValue<string>() ?? MessageForState(state);
            var components = new Dictionary<string, string>();
            if (o?["components"] is JsonObject parts)
                foreach (var (name, value) in parts)
                    if (value is JsonValue v && v.TryGetValue<string>(out var text)) components[name] = text;
            return new AIReadiness(identity, state, message)
            {
                HelperVersion = o?["version"] is JsonValue ver && ver.TryGetValue<string>(out var version) && version.Length > 0 ? version : null,
                Components = components,
            };
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            return new AIReadiness(false, "Error", $"The {AppInfo.HelperName} gave an answer that couldn't be read: {line.Trim()}");
        }
    }

    internal static string? MessageForState(string state) => state switch
    {
        "Ready" or "NotReady" => null,
        "NotSupportedOnCurrentSystem" => "Windows' image description needs a Copilot+ PC, with an NPU, and a recent version of Windows 11.",
        "DisabledByUser" => "Windows' AI features are turned off on this PC. They can be turned on in Settings, Privacy & security.",
        _ => $"Windows' image description isn't available on this PC ({state}).",
    };

    /// <summary>Describes a picture. <paramref name="jpegAtSize"/> makes the JPEG to send at a given longest side, so an
    /// internal error can be tried again smaller.</summary>
    public Task<string> DescribeAsync(Func<uint, Task<byte[]>> jpegAtSize, string kind, CancellationToken cancel = default) =>
        WithRetriesAsync(jpegAtSize, async jpeg =>
        {
            var id = Interlocked.Increment(ref _nextId);
            return ParseResponse(await SendAsync(BuildRequest(id, kind, jpeg), id, cancel), id);
        });

    /// <summary>The text in a picture, line by line, from Windows AI's text recognizer (Copilot+ PCs). Sent as PNG: JPEG's
    /// blur around letters costs OCR more than it costs a description.</summary>
    public async Task<IReadOnlyList<string>> ReadTextAsync(byte[] png, CancellationToken cancel = default)
    {
        var id = Interlocked.Increment(ref _nextId);
        return ParseLines(await SendAsync(BuildReadRequest(id, png), id, cancel), id);
    }

    /// <summary>
    /// Has Windows load its models before they're needed: the first description in a session took 6 to 18 seconds on a
    /// Copilot+ PC, and later ones about 3 (measured 15 Oct 2026). Describes and reads a tiny blank picture through this
    /// helper, so the helper is running and Windows has both models loaded when the first real picture comes. A request
    /// made meanwhile queues behind it, so it waits no longer than it would have for the cold load anyway. One try each,
    /// within <see cref="WarmUpTimeout"/>: a warm-up that fails isn't retried at other sizes, as a real picture is, so it
    /// can't hold the helper for minutes. Returns how long it took; throws when it didn't work.
    /// </summary>
    public async Task<TimeSpan> WarmUpAsync(CancellationToken cancel = default)
    {
        var started = Stopwatch.StartNew();
        var outer = cancel;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        limit.CancelAfter(WarmUpTimeout);
        cancel = limit.Token;
        var (jpeg, png) = await Task.Run(() =>
        {
            var blank = System.Windows.Media.Imaging.BitmapSource.Create(64, 64, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null,
                Enumerable.Repeat((byte)200, 64 * 64 * 4).ToArray(), 64 * 4);
            blank.Freeze();
            return (Pictures.Jpeg(blank, 64), Pictures.Png(blank, 64));
        }, cancel);
        try
        {
            try
            {
                var id = Interlocked.Increment(ref _nextId);
                ParseResponse(await SendAsync(BuildRequest(id, "brief", jpeg), id, cancel), id);
            }
            catch (DescriptionException ex) when (ex.Code is "too_much_text" or "content_filtered" or "empty")
            {
                // A verdict on the blank picture: the model loaded all the same.
            }
            try
            {
                await ReadTextAsync(png, cancel);
            }
            catch (DescriptionException ex) when (ex.Code is "not_supported" or "disabled_by_user" or "not_ready")
            {
                // A PC without Windows AI's text recognizer: the built-in OCR reads text there, and needs no warming.
            }
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested && !outer.IsCancellationRequested)
        {
            // Its own limit, not Quick Describe closing: Windows isn't answering, and a request waiting behind gives up too.
            Interlocked.Increment(ref _timeouts);
            throw new DescriptionException("timeout", NotRespondingMessage);
        }
        return started.Elapsed;
    }

    /// <summary>The longest warming up may take: a cold load is 6 to 18 seconds.</summary>
    internal static TimeSpan WarmUpTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Tries the first size, then smaller ones while the model reports an internal error. Any other failure
    /// (content filter, too much text) is final.</summary>
    internal static async Task<T> WithRetriesAsync<T>(Func<uint, Task<byte[]>> jpegAtSize, Func<byte[], Task<T>> describe)
    {
        DescriptionException? last = null;
        foreach (var size in new[] { FirstSize }.Concat(RetrySizes))
        {
            var jpeg = await jpegAtSize(size);
            try
            {
                return await describe(jpeg);
            }
            catch (DescriptionException ex) when (ex.Code == "internal_error")
            {
                last = ex;
            }
        }
        throw last!;
    }

    /// <summary>Sends one request line and gives back the helper's answer line, one request at a time.</summary>
    private async Task<string> SendAsync(string request, int id, CancellationToken cancel)
    {
        var timeouts = Volatile.Read(ref _timeouts);
        await _gate.WaitAsync(cancel);
        try
        {
            // The request ahead wasn't answered: this one would very likely wait as long again, while describing's text
            // could come from the built-in OCR now. A later request tries a fresh helper.
            if (Volatile.Read(ref _timeouts) != timeouts)
                throw new DescriptionException("timeout", NotRespondingMessage);
            var server = EnsureServer();
            string? line;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(_timeout);
            try
            {
                await server.WriteLineAsync(request, timeout.Token);
                line = await server.ReadLineAsync(timeout.Token);
            }
            catch (Exception ex)
            {
                // Given up or failed: the helper may still answer this request later, and that answer would then be read as
                // the next one's. A fresh helper keeps answers and requests in step.
                StopServer();
                if (!cancel.IsCancellationRequested) Log.Error("helper", "A request to the helper failed; it was stopped", ex);
                if (ex is OperationCanceledException && !cancel.IsCancellationRequested)
                {
                    Interlocked.Increment(ref _timeouts);
                    throw new DescriptionException("timeout", NotRespondingMessage);
                }
                if (ex is IOException)
                    throw new DescriptionException("internal_error", $"The {AppInfo.HelperName} stopped unexpectedly.");
                throw;
            }
            if (line is null)
            {
                Log.Write("helper", "The helper stopped without answering");
                StopServer();
                throw new DescriptionException("internal_error", $"The {AppInfo.HelperName} stopped unexpectedly.");
            }
            if (!IsAnswerTo(line, id))
            {
                // Not an answer to this request: the helper and this are out of step, so start a fresh one next time.
                Log.Write("helper", "The helper's answer didn't match the request; it was stopped");
                StopServer();
                throw new DescriptionException("bad_response", $"The {AppInfo.HelperName} gave an answer that couldn't be read.");
            }
            return line;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static string BuildRequest(int id, string kind, byte[] jpeg) =>
        new JsonObject { ["id"] = id, ["kind"] = kind, ["mime"] = "image/jpeg", ["image"] = Convert.ToBase64String(jpeg) }.ToJsonString();

    /// <summary>Whether a line is a JSON answer to request <paramref name="id"/> (or to an unreadable request, id 0).</summary>
    internal static bool IsAnswerTo(string line, int id)
    {
        try
        {
            return JsonNode.Parse(line) is JsonObject o && o["id"] is JsonValue v && v.TryGetValue<int>(out var answered) && (answered == id || answered == 0);
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    internal static string BuildReadRequest(int id, byte[] png) =>
        new JsonObject { ["id"] = id, ["op"] = "read", ["mime"] = "image/png", ["image"] = Convert.ToBase64String(png) }.ToJsonString();

    /// <summary>The lines from a read's response line, or a <see cref="DescriptionException"/> with the helper's code.</summary>
    internal static IReadOnlyList<string> ParseLines(string line, int expectedId)
    {
        var o = ParseAnswer(line, expectedId);
        try
        {
            if (o["lines"] is not JsonArray lines)
                throw new DescriptionException("bad_response", $"The {AppInfo.HelperName} gave an answer that couldn't be read.");
            return lines.Select(l => l?.GetValue<string>().Trim() ?? "").Where(l => l.Length > 0).ToList();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            throw new DescriptionException("bad_response", $"The {AppInfo.HelperName} gave an answer that couldn't be read.");
        }
    }

    /// <summary>The description from a response line, or a <see cref="DescriptionException"/> with the helper's code.</summary>
    internal static string ParseResponse(string line, int expectedId)
    {
        var o = ParseAnswer(line, expectedId);
        try
        {
            return (o["text"]?.GetValue<string>() ?? "").Trim();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            throw new DescriptionException("bad_response", $"The {AppInfo.HelperName} gave an answer that couldn't be read.");
        }
    }

    /// <summary>A successful answer to request <paramref name="expectedId"/>, or a <see cref="DescriptionException"/>: the
    /// helper's own code when it failed, bad_response when the answer can't be read or is another request's.</summary>
    private static JsonObject ParseAnswer(string line, int expectedId)
    {
        JsonObject? o;
        try { o = JsonNode.Parse(line) as JsonObject; }
        catch (System.Text.Json.JsonException) { o = null; }
        if (o is null) throw new DescriptionException("bad_response", $"The {AppInfo.HelperName} gave an answer that couldn't be read.");
        try
        {
            if (o["id"]?.GetValue<int>() is { } id && id != expectedId && id != 0)
                throw new DescriptionException("bad_response", $"The {AppInfo.HelperName} answered a different request.");
            if (o["ok"]?.GetValue<bool>() == true)
                return o;
            var code = o["code"]?.GetValue<string>() ?? "internal_error";
            var message = o["message"]?.GetValue<string>() ?? "Windows couldn't describe this picture.";
            throw new DescriptionException(code, message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            throw new DescriptionException("bad_response", $"The {AppInfo.HelperName} gave an answer that couldn't be read.");
        }
    }

    private IHelperConnection EnsureServer()
    {
        if (_server is { HasExited: false }) return _server;
        _server?.Dispose();
        _server = null;
        _server = _start();
        return _server;
    }

    /// <summary>Starts the helper's --serve. A helper that can't be started (missing, or an alias Windows can't open) is
    /// not_installed, as if it weren't there, so text reading falls back to the built-in OCR.</summary>
    private static IHelperConnection StartServer()
    {
        if (!IsInstalled) throw new DescriptionException("not_installed", NotInstalledMessage);
        Process? process;
        try
        {
            process = Process.Start(StartInfo("--serve"));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            throw new DescriptionException("not_installed", $"The {AppInfo.HelperName} didn't start: {ex.Message.Trim()}");
        }
        if (process is null) throw new DescriptionException("not_installed", NotInstalledMessage);
        // Its stderr is only progress notes ("getting the model ready"); read it so it never fills up and blocks.
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        return new ProcessConnection(process);
    }

    private void StopServer()
    {
        _server?.Kill();
        _server?.Dispose();
        _server = null;
    }

    private static ProcessStartInfo StartInfo(string mode)
    {
        var utf8 = new UTF8Encoding(false);
        return new ProcessStartInfo(HelperPath, mode)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = utf8,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
        };
    }

    private static async Task<string?> RunOnceAsync(string mode, TimeSpan timeout, CancellationToken cancel)
    {
        using var process = Process.Start(StartInfo(mode)) ?? throw new InvalidOperationException(NotInstalledMessage);
        process.StandardInput.Close();
        var stderr = process.StandardError.ReadToEndAsync(cancel);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        limit.CancelAfter(timeout);
        try
        {
            var output = await process.StandardOutput.ReadToEndAsync(limit.Token);
            await process.WaitForExitAsync(limit.Token);
            await stderr;
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (Exception) { /* already gone */ }
            if (cancel.IsCancellationRequested) throw;
            return null;
        }
    }

    /// <summary>Ends the helper. A request still waiting for it fails, as when the helper stops. (The gate isn't
    /// disposed: such a request still releases it.)</summary>
    public void Dispose() => StopServer();
}

/// <summary>The running helper, as <see cref="WindowsAI"/> talks to it: one line in, one line out. A fake in tests.</summary>
internal interface IHelperConnection : IDisposable
{
    bool HasExited { get; }
    Task WriteLineAsync(string line, CancellationToken cancel);

    /// <summary>The next answer line, or null when the helper has stopped.</summary>
    Task<string?> ReadLineAsync(CancellationToken cancel);

    /// <summary>Ends the helper now, whatever it's doing.</summary>
    void Kill();
}

internal sealed class ProcessConnection(Process process) : IHelperConnection
{
    public bool HasExited
    {
        get
        {
            try { return process.HasExited; }
            catch (InvalidOperationException) { return true; }
        }
    }

    public async Task WriteLineAsync(string line, CancellationToken cancel)
    {
        await process.StandardInput.WriteAsync(line.AsMemory(), cancel);
        await process.StandardInput.WriteAsync("\n".AsMemory(), cancel);
        await process.StandardInput.FlushAsync(cancel);
    }

    public Task<string?> ReadLineAsync(CancellationToken cancel) => process.StandardOutput.ReadLineAsync(cancel).AsTask();

    public void Kill()
    {
        try
        {
            // Killed outright rather than waited for: a helper partway through a description would hold things up.
            process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // Already gone.
        }
    }

    public void Dispose() => process.Dispose();
}
