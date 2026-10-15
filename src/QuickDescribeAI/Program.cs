// Quick Describe's Windows AI helper: describes pictures with Windows' on-device model on a Copilot+ PC.
// Adapted from My Photo Library Described's helper (PhotoFinderAI, itself from Image Description Toolkit's), same protocol,
// plus "op": "read" for Windows AI's text recognizer.
//
//   quickdescribe-ai --check                 readiness, as one JSON line
//   quickdescribe-ai --details               --check plus text recognition's and Phi Silica's states, for Check Windows AI
//   quickdescribe-ai --prepare               make the model ready (may download it), as one JSON line
//   quickdescribe-ai --serve                 JSON lines on stdin and stdout, for Quick Describe (see Server.cs)
//   quickdescribe-ai --describe <picture> [kind ...]   describe one picture, for people
//   quickdescribe-ai --read <picture>                  the text in one picture, line by line
//   quickdescribe-ai --version
//
// Windows only lets a packaged app use the model, so this must run with its package's identity. Installed, Quick
// Describe starts it from its own package's AI folder, which gives it that identity; in development it starts it as
// quickdescribe-ai (the command a registered helper's package adds). Started without identity, as a person might from
// its folder, it starts itself again through the command, tied to this process so it can't outlive it.
using System.ComponentModel;
using QuickDescribeAI;

const string Alias = "quickdescribe-ai.exe";
const string RelaunchedVariable = "QUICKDESCRIBE_AI_RELAUNCHED";

Console.OutputEncoding = new System.Text.UTF8Encoding(false);
Console.InputEncoding = new System.Text.UTF8Encoding(false);
var mode = args.FirstOrDefault()?.ToLowerInvariant() ?? "--help";

if (mode is "--version")
{
    Line(typeof(Protocol).Assembly.GetName().Version?.ToString(3) ?? "");
    return 0;
}
if (mode is "--help" or "-h" or "/?")
{
    Line("Usage: quickdescribe-ai --check | --details | --prepare | --serve | --describe <picture> [kind ...] | --read <picture> | --version");
    Line($"Kinds: {string.Join(", ", Protocol.Kinds)}");
    return 0;
}

if (!HasIdentity())
    return Relaunch();

try
{
    switch (mode)
    {
        case "--check":
            Line(Protocol.Readiness(true, Describer.ReadyState()));
            return 0;

        case "--details":
            // For Quick Describe's Check Windows AI: --check plus the other AI features' states. Kept apart from --check, which
            // every description waits on.
            Line(Protocol.Readiness(true, Describer.ReadyState(), components: Describer.OtherStates()));
            return 0;

        case "--prepare":
        {
            AnnounceDownload();
            var failure = await Describer.PrepareAsync();
            Line(Protocol.Readiness(true, Describer.ReadyState(), failure?.Message));
            return failure is null ? 0 : 1;
        }

        case "--serve":
        {
            using var describer = new Describer();
            await Server.RunAsync(Console.In, Console.Out, describer.DescribeAsync, describer.Reset,
                r => describer.ReadTextAsync(r.Id, r.Mime, r.Image), describer.ResetRecognizer);
            return 0;
        }

        case "--describe":
            return await DescribeForPeopleAsync(args.Skip(1).ToArray());

        case "--read":
        {
            if (args.Length < 2 || !MimeFor(args[1], out var mime))
            {
                Console.Error.WriteLine("Usage: quickdescribe-ai --read <JPEG, PNG, BMP, GIF or TIFF picture>");
                return 2;
            }
            using var describer = new Describer();
            foreach (var line in await describer.ReadTextAsync(0, mime, await File.ReadAllBytesAsync(Path.GetFullPath(args[1]))))
                Line(line);
            return 0;
        }

        default:
            Console.Error.WriteLine($"Unknown option {args[0]}. Try --help.");
            return 2;
    }
}
catch (Exception ex)
{
    // One readable line, never a .NET stack trace: Quick Describe reads --check and --prepare as JSON, and
    // a person reading --describe with a screen reader doesn't want a wall of symbols.
    var message = $"Windows' image description failed: {ex.Message.Trim()} ({ex.GetType().Name}, 0x{ex.HResult:X8})";
    if (mode is "--check" or "--details" or "--prepare") Line(Protocol.Readiness(true, "Error", message));
    else Console.Error.WriteLine(message);
    return 1;
}

// Started without identity: run again through the command that has it, passing stdin and
// stdout through, and wait. Never twice: if the command itself somehow lacks identity, say so
// rather than start copies forever.
int Relaunch()
{
    const string notInstalled = "The Windows AI helper isn't installed as a package, and Windows only lets a package use its model. " +
                                $"Install it with {QuickDescribe.AppInfo.Name}, or for development run scripts\\build-ai-helper.ps1 -Register.";
    var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", Alias);
    if (Environment.GetEnvironmentVariable(RelaunchedVariable) == "1" || !File.Exists(alias))
    {
        if (mode is "--check" or "--details" or "--prepare") Line(Protocol.Readiness(false, "NoIdentity", notInstalled));
        else Console.Error.WriteLine(notInstalled);
        return 1;
    }
    var start = new System.Diagnostics.ProcessStartInfo(alias) { UseShellExecute = false };
    foreach (var a in args) start.ArgumentList.Add(a);
    start.Environment[RelaunchedVariable] = "1";
    try
    {
        // If this process is killed, the job's handle closes and Windows ends the child too, so
        // it can't be left holding the caller's pipes. The child is untied for the moment between
        // starting and joining; a kill in exactly that moment is the one way it can outlive this.
        using var job = KillOnCloseJob.Create();
        using var child = System.Diagnostics.Process.Start(start)!;
        if (job is null || !job.Add(child))
            Console.Error.WriteLine("Note: couldn't tie the restarted helper to this one; if this is ended, end quickdescribe-ai too.");
        child.WaitForExit();
        return child.ExitCode;
    }
    catch (Win32Exception ex)
    {
        var message = $"{notInstalled} ({ex.Message.Trim()})";
        if (mode is "--check" or "--details" or "--prepare") Line(Protocol.Readiness(false, "NoIdentity", message));
        else Console.Error.WriteLine(message);
        return 1;
    }
}

static async Task<int> DescribeForPeopleAsync(string[] rest)
{
    if (rest.Length == 0)
    {
        Console.Error.WriteLine("Usage: quickdescribe-ai --describe <picture> [kind ...]");
        return 2;
    }
    var path = Path.GetFullPath(rest[0]);
    var kinds = rest.Length > 1 ? rest.Skip(1).ToArray() : [Protocol.Kinds[0]];
    byte[] image;
    try { image = await File.ReadAllBytesAsync(path); }
    catch (Exception ex) { Console.Error.WriteLine($"Can't read {path}: {ex.Message}"); return 1; }

    if (!MimeFor(path, out var mime))
    {
        Console.Error.WriteLine($"{Path.GetFileName(path)}: the helper reads JPEG, PNG, BMP, GIF or TIFF pictures, named with their usual extension.");
        return 1;
    }
    AnnounceDownload();
    using var describer = new Describer();
    var status = 0;
    foreach (var kind in kinds)
    {
        var started = DateTime.UtcNow;
        try
        {
            var request = Protocol.Validate(0, kind, mime, image);
            var text = await describer.DescribeAsync(request);
            Line($"{char.ToUpperInvariant(request.Kind[0])}{request.Kind[1..]} ({(DateTime.UtcNow - started).TotalSeconds:0.0} s):");
            Line(text);
        }
        catch (ProtocolException ex)
        {
            Line($"{kind}: {ex.Message}");
            status = 1;
        }
        catch (Exception ex)
        {
            Line($"{kind}: Windows couldn't describe it: {ex.Message.Trim()}");
            describer.Reset();
            status = 1;
        }
        Line("");
    }
    return status;
}

static bool MimeFor(string path, out string mime)
{
    mime = Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".bmp" => "image/bmp",
        ".gif" => "image/gif", ".tif" or ".tiff" => "image/tiff", _ => "",
    };
    return mime.Length > 0;
}

// Getting the model ready can take minutes the first time, with nothing else to show for it.
// On stderr, so a caller reading the JSON on stdout never sees it.
static void AnnounceDownload()
{
    if (Describer.ReadyState() == "NotReady")
        Console.Error.WriteLine("Getting Windows' image description model ready. The first time, this can take a few minutes.");
}

static void Line(string text) => Console.Out.Write(text + "\n");

static bool HasIdentity()
{
    try { _ = Windows.ApplicationModel.Package.Current.Id; return true; }
    catch (InvalidOperationException) { return false; }
}
