using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;

namespace QuickDescribe.Services;

/// <summary>Edit > Copy Image and Description: the picture and its description on the clipboard together, in the forms
/// apps paste. Each app takes the richest one it understands: Word, Outlook and web email the HTML (the picture with the
/// description as its alt text, and the description and text after it), Notepad and text boxes the text, Paint the
/// picture, File Explorer the picture file with the description inside it. Checked by pasting into Word, which made the
/// picture with its alt text and the paragraphs after it, whether the HTML's picture was a data: or a file: URI and with
/// the file there too (Kelly's PC, 24 Nov 2026). The picture is in the HTML itself, a data: URI, since web pages may
/// not show a file: one.</summary>
public static class CopyWithDescription
{
    /// <summary>The HTML fragment: the picture, its alt text the description (shortened as a saved picture's is), then the
    /// caption, a paragraph for each part, its lines kept apart. Without a description the alt text is what was described
    /// (<paramref name="what"/>, "Screen"): an empty one would mark the picture as decoration, which screen readers skip.</summary>
    public static string HtmlFragment(EmbeddedDescription? fields, string imageSource, string what)
    {
        var html = new StringBuilder();
        html.Append("<img src=\"").Append(Escaped(imageSource)).Append("\" alt=\"")
            .Append(Escaped(fields?.AltText ?? what)).Append("\">");
        if (fields is not null)
        {
            // Lines the model ended with a bare line feed are lines too.
            var paragraphs = fields.Caption.ReplaceLineEndings("\n").Split("\n\n");
            foreach (var paragraph in paragraphs)
            {
                var lines = paragraph.Split('\n').Select(Escaped);
                html.Append("<p>").Append(string.Join("<br>", lines)).Append("</p>");
            }
        }
        return html.ToString();
    }

    /// <summary>Only what HTML needs escaped. The rest stays as it is, in UTF-8 (WebUtility.HtmlEncode would make every
    /// accented letter a numeric reference).</summary>
    internal static string Escaped(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private const string Before = "<html><head><meta charset=\"utf-8\"></head><body><!--StartFragment-->";
    private const string After = "<!--EndFragment--></body></html>";
    private const string Header = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";

    /// <summary>The clipboard's HTML Format: a header giving, in UTF-8 bytes, where the HTML and the fragment start and end,
    /// then the HTML, ending in a zero byte. Given as bytes so they reach the clipboard as they are.</summary>
    public static byte[] ClipboardHtml(string fragment)
    {
        var utf8 = new UTF8Encoding(false);
        var startHtml = utf8.GetByteCount(string.Format(Header, 0, 0, 0, 0));
        var startFragment = startHtml + utf8.GetByteCount(Before);
        var endFragment = startFragment + utf8.GetByteCount(fragment);
        var endHtml = endFragment + utf8.GetByteCount(After);
        var text = string.Format(Header, startHtml, endHtml, startFragment, endFragment) + Before + fragment + After;
        var bytes = new byte[utf8.GetByteCount(text) + 1];
        utf8.GetBytes(text, 0, text.Length, bytes, 0);
        return bytes;
    }

    /// <summary>A data: URI of a picture file's bytes.</summary>
    public static string DataUri(byte[] file, string extension) =>
        $"data:{(extension == ".jpg" ? "image/jpeg" : "image/png")};base64,{Convert.ToBase64String(file)}";

    /// <summary>Where the picture file pasted into File Explorer or an email is kept until the next copy. Not in
    /// %LOCALAPPDATA%: a packaged app's new folders there are kept in its package, where other apps don't look, so it's the
    /// package's own LocalCache, a real folder they can read; outside a package, the temporary folder.</summary>
    public static string Folder
    {
        get
        {
            try { return Path.Combine(Windows.Storage.ApplicationData.Current.LocalCacheFolder.Path, "Copied"); }
            catch (Exception) { return Path.Combine(Path.GetTempPath(), AppInfo.Name, "Copied"); } // not installed as a package
        }
    }

    /// <summary>Saves the picture, with its description, in <paramref name="folder"/>, named as Save Described Image
    /// suggests. Returns its path.</summary>
    public static string SaveFile(Picture picture, DateTime taken, EmbeddedDescription? fields, string folder) =>
        Pictures.SaveToFolder(picture, taken, folder, fields);

    /// <summary>Removes the earlier copies' files once a newer one is on the clipboard: only the newest is pasted from. Not
    /// before, so a copy that fails (the clipboard busy) leaves the last one's file for the clipboard that still names it.
    /// One still open where it's being pasted is left for next time.</summary>
    public static void RemoveOlder(string folder, string keep)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var old in Directory.EnumerateFiles(folder))
        {
            if (string.Equals(old, keep, StringComparison.OrdinalIgnoreCase)) continue;
            try { File.Delete(old); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* kept */ }
        }
    }

    /// <summary>Everything for the clipboard. The slow parts (encoding, saving the file), so it can run off the UI thread;
    /// <see cref="ClipboardData"/> then puts it together on it.</summary>
    public sealed record Prepared(BitmapSource Bitmap, MemoryStream Png, byte[] Html, string? Text, string File);

    public static Prepared Prepare(Picture picture, DateTime taken, EmbeddedDescription? fields, string folder)
    {
        var (bitmap, png) = Pictures.ForClipboard(picture.Image);
        var file = SaveFile(picture, taken, fields, folder);
        var what = picture.Source.EndsWith(ScreenCapture.NoPlaceNote, StringComparison.Ordinal)
            ? picture.Source[..^ScreenCapture.NoPlaceNote.Length]
            : picture.Source;
        var html = ClipboardHtml(HtmlFragment(fields, DataUri(System.IO.File.ReadAllBytes(file), Path.GetExtension(file)), what));
        // The text is the caption a saved picture holds: the description, then "Text in the picture:" and the text read.
        return new Prepared(bitmap, png, html, fields?.Caption, file);
    }

    public static DataObject ClipboardData(Prepared prepared)
    {
        var data = Pictures.ClipboardData(prepared.Bitmap, prepared.Png);
        data.SetData(DataFormats.Html, new MemoryStream(prepared.Html), false);
        if (prepared.Text is { } text) data.SetData(DataFormats.UnicodeText, text, false);
        data.SetData(DataFormats.FileDrop, new[] { prepared.File }, false);
        return data;
    }
}
