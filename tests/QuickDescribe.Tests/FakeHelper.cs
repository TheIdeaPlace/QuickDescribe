using System.Text.Json.Nodes;
using System.Threading.Channels;
using QuickDescribe.Services;

namespace QuickDescribe.Tests;

/// <summary>
/// A stand-in for the helper's --serve: each request line gets the answer <see cref="Answer"/> gives (null: the helper
/// stops without answering). <see cref="Hold"/> keeps answers back until released, for timeouts and requests in flight.
/// </summary>
internal sealed class FakeHelper : IHelperConnection
{
    private readonly Channel<string?> _answers = Channel.CreateUnbounded<string?>();

    public Func<JsonObject, string?> Answer { get; set; } = r => Ok(r);
    public List<JsonObject> Requests { get; } = [];
    public TaskCompletionSource? Hold { get; set; }
    public bool Killed { get; private set; }
    public bool Disposed { get; private set; }
    public bool HasExited => Killed;

    /// <summary>An answer to a request: lines for a read, a description otherwise.</summary>
    public static string Ok(JsonObject request, string text = "A description.") =>
        (string?)request["op"] == "read"
            ? new JsonObject { ["id"] = (int)request["id"]!, ["ok"] = true, ["lines"] = new JsonArray("Line one", "Line two") }.ToJsonString()
            : new JsonObject { ["id"] = (int)request["id"]!, ["ok"] = true, ["text"] = text }.ToJsonString();

    public static string Fail(JsonObject request, string code) =>
        new JsonObject { ["id"] = (int)request["id"]!, ["ok"] = false, ["code"] = code, ["message"] = $"Failed: {code}" }.ToJsonString();

    public async Task WriteLineAsync(string line, CancellationToken cancel)
    {
        if (Killed) throw new IOException("The pipe has been ended.");
        var request = JsonNode.Parse(line)!.AsObject();
        Requests.Add(request);
        var answer = Answer(request);
        if (Hold is { } hold)
            _ = hold.Task.ContinueWith(_ => _answers.Writer.TryWrite(answer), TaskScheduler.Default);
        else
            await _answers.Writer.WriteAsync(answer, cancel);
    }

    public async Task<string?> ReadLineAsync(CancellationToken cancel) => await _answers.Reader.ReadAsync(cancel);

    public void Kill()
    {
        Killed = true;
        _answers.Writer.TryWrite(null); // a reader waiting now sees the end, as from a real pipe
    }

    public void Dispose() => Disposed = true;
}

/// <summary>Starts fake helpers for a <see cref="WindowsAI"/>, keeping each one it started.</summary>
internal sealed class FakeHelpers
{
    public List<FakeHelper> Started { get; } = [];
    public Action<FakeHelper>? Setup { get; set; }
    public Exception? StartFailure { get; set; }

    public WindowsAI Client(TimeSpan? timeout = null) => new(Start, timeout ?? TimeSpan.FromSeconds(10));

    private IHelperConnection Start()
    {
        if (StartFailure is { } failure) throw failure;
        var helper = new FakeHelper();
        Setup?.Invoke(helper);
        Started.Add(helper);
        return helper;
    }
}
