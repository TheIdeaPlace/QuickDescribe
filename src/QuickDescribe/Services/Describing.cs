using System.Windows.Media.Imaging;

namespace QuickDescribe.Services;

/// <summary>Describes a picture and reads its text, and puts the two together.</summary>
public static class Describing
{
    /// <summary>
    /// Both, started together. They share the one helper, which takes a request at a time, so the wait is the two added
    /// together; the text, the quicker, usually goes first. Neither failing stops the other: a screen the model declines
    /// for its text still gets its text read. Cancelling gives up both. Call on the UI thread.
    /// </summary>
    /// <param name="check">For tests: whether Windows AI can describe. Defaults to asking the helper.</param>
    public static async Task<Result> RunAsync(BitmapSource image, string kind, WindowsAI ai, CancellationToken cancel = default,
        Func<CancellationToken, Task<AIReadiness>>? check = null)
    {
        var describe = DescribeAsync(image, kind, ai, check ?? WindowsAI.CheckAsync, cancel);
        var read = ReadAsync(image, ai, cancel);
        var (description, code, problem) = await describe;
        var (lines, textProblem) = await read;
        cancel.ThrowIfCancellationRequested();
        return new Result(description, code, problem, lines, textProblem);
    }

    private static async Task<(string? Text, string? Code, string? Problem)> DescribeAsync(BitmapSource image, string kind, WindowsAI ai,
        Func<CancellationToken, Task<AIReadiness>> check, CancellationToken cancel)
    {
        try
        {
            var readiness = await check(cancel);
            if (!readiness.Usable)
            {
                Log.Write("describe", $"Not described: Windows AI is {readiness.State}: {readiness.Message}");
                return (null, "unavailable", readiness.Message);
            }
            // The picture is frozen, so another thread can encode it.
            var text = await ai.DescribeAsync(size => Task.Run(() => Pictures.Jpeg(image, size), cancel), kind, cancel);
            return text.Length > 0 ? (text, null, null) : (null, "empty", "Windows gave an empty description.");
        }
        catch (DescriptionException ex)
        {
            Log.Write("describe", $"Not described: {ex.Code}: {ex.Message}");
            return (null, ex.Code, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error("describe", "Describing failed", ex);
            return (null, "error", $"{ex.Message.Trim()} ({ex.GetType().Name})");
        }
    }

    private static async Task<(IReadOnlyList<string>? Lines, string? Problem)> ReadAsync(BitmapSource image, WindowsAI ai, CancellationToken cancel)
    {
        try
        {
            return (await TextRecognition.ReadLinesAsync(image, ai, cancel), null);
        }
        catch (PictureException ex)
        {
            Log.Write("text", ex.Message);
            return (null, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error("text", "Reading the text failed", ex);
            return (null, $"{ex.Message.Trim()} ({ex.GetType().Name})");
        }
    }
}
