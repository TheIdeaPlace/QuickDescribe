using System.Windows.Media.Imaging;

namespace QuickDescribe.Services;

/// <summary>Describes a picture and reads its text at the same time, and puts the two together.</summary>
public static class Describing
{
    /// <summary>
    /// Both at once: Windows' AI describes while OCR reads, so the slower of the two sets the wait. Neither failing stops
    /// the other: a screen the model declines for its text still gets its text read. Call on the UI thread.
    /// </summary>
    public static async Task<Result> RunAsync(BitmapSource image, string kind, WindowsAI ai, CancellationToken cancel = default)
    {
        var describe = DescribeAsync(image, kind, ai, cancel);
        var read = ReadAsync(image);
        var (description, code, problem) = await describe;
        var (lines, textProblem) = await read;
        return new Result(description, code, problem, lines, textProblem);
    }

    private static async Task<(string? Text, string? Code, string? Problem)> DescribeAsync(BitmapSource image, string kind, WindowsAI ai,
        CancellationToken cancel)
    {
        var readiness = await WindowsAI.CheckAsync(cancel);
        if (!readiness.Usable) return (null, "unavailable", readiness.Message);
        try
        {
            // The picture is frozen, so another thread can encode it.
            var text = await ai.DescribeAsync(size => Task.Run(() => Pictures.Jpeg(image, size), cancel), kind, cancel);
            return text.Length > 0 ? (text, null, null) : (null, "empty", "Windows gave an empty description.");
        }
        catch (DescriptionException ex)
        {
            return (null, ex.Code, ex.Message);
        }
    }

    private static async Task<(IReadOnlyList<string>? Lines, string? Problem)> ReadAsync(BitmapSource image)
    {
        try
        {
            return (await TextRecognition.ReadLinesAsync(image), null);
        }
        catch (PictureException ex)
        {
            return (null, ex.Message);
        }
        catch (Exception ex)
        {
            return (null, $"{ex.Message.Trim()} ({ex.GetType().Name})");
        }
    }
}
