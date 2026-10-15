namespace QuickDescribe.Services;

/// <summary>One describing request: its number, and the token that gives it up.</summary>
public sealed record Request(int Number, CancellationToken Token);

/// <summary>
/// Describing, one request at a time: starting one gives up the one before (its result would only be thrown away, and
/// the helper would do its work first), and only the latest one's result is shown. Messages that aren't requests (Help,
/// F1) don't touch this, so they never lose a result that's on its way.
/// </summary>
public sealed class Requests
{
    private CancellationTokenSource? _current;
    private int _number;

    /// <summary>Starts a request, giving up the one before.</summary>
    public Request Start()
    {
        Cancel();
        _current = new CancellationTokenSource();
        return new Request(++_number, _current.Token);
    }

    /// <summary>Whether a request is the latest, and hasn't been given up: only then is its result shown.</summary>
    public bool IsCurrent(Request request) => request.Number == _number && !request.Token.IsCancellationRequested;

    /// <summary>Whether a request is still being worked on.</summary>
    public bool IsRunning => _current is { IsCancellationRequested: false };

    /// <summary>A request is done: it stops counting as running (if it's the latest).</summary>
    public void Finish(Request request)
    {
        if (request.Number != _number) return;
        _current = null;
    }

    /// <summary>Gives up the request being worked on, if any. Returns whether there was one.</summary>
    public bool Cancel()
    {
        if (_current is null) return false;
        // Not disposed: work given up may still be finishing with the token, and a source without a timer holds nothing.
        var was = !_current.IsCancellationRequested;
        _current.Cancel();
        _current = null;
        return was;
    }
}
