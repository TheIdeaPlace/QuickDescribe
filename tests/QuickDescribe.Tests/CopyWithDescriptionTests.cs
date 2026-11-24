using System.Text;
using System.Windows;
using QuickDescribe.Services;
using QuickDescribe.Views;

namespace QuickDescribe.Tests;

public class CopyWithDescriptionTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("QuickDescribeCopyTests").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    private static readonly DateTime Taken = new(2026, 11, 24, 14, 5, 12);

    private static EmbeddedDescription Fields(string? description, params string[] lines) =>
        EmbeddedDescription.For(new Result(description, description is null ? "too_much_text" : null, null, lines.Length > 0 ? lines : null, null))!;

    [Fact]
    public void Html_IsThePictureWithItsAltText_ThenEachPart()
    {
        var fields = Fields("A café sign says \"Open\" & <welcome>.", "OPEN", "9 to 5");
        var html = CopyWithDescription.HtmlFragment(fields, "data:image/png;base64,AAAA", "Screen");
        Assert.Equal(
            "<img src=\"data:image/png;base64,AAAA\" alt=\"A café sign says &quot;Open&quot; &amp; &lt;welcome&gt;.\">" +
            "<p>A café sign says &quot;Open&quot; &amp; &lt;welcome&gt;.</p>" +
            "<p>Text in the picture:<br>OPEN<br>9 to 5</p>",
            html);
    }

    [Fact]
    public void Html_AltTextIsShortenedAsASavedPictures_ButTheParagraphsHaveItAll()
    {
        var sentence = "A wooden table holds a stack of books, a lamp and a cup of tea beside an open window. ";
        var description = string.Concat(Enumerable.Repeat(sentence, 5)).Trim();
        var fields = Fields(description);
        var html = CopyWithDescription.HtmlFragment(fields, "x", "Screen");
        Assert.Contains($"alt=\"{fields.AltText}\"", html);
        Assert.True(fields.AltText.Length < description.Length);
        Assert.EndsWith($"<p>{description}</p>", html);
    }

    [Fact]
    public void Html_WithoutADescription_IsThePictureAlone_NamedForWhatItIs()
    {
        // Not alt="", which would mark it as decoration for a screen reader to skip.
        Assert.Equal("<img src=\"x\" alt=\"Active window, Notes &amp; Lists\">",
            CopyWithDescription.HtmlFragment(null, "x", "Active window, Notes & Lists"));
    }

    [Fact]
    public void Html_LinesEndingInALineFeedAloneAreLinesToo()
    {
        var html = CopyWithDescription.HtmlFragment(Fields("First line.\nSecond line.\n\nNext part."), "x", "Screen");
        Assert.EndsWith("<p>First line.<br>Second line.</p><p>Next part.</p>", html);
    }

    [Fact]
    public void ClipboardHtml_OffsetsCountUtf8Bytes()
    {
        // Letters and an emoji that take more than one byte, so a count of characters would be wrong.
        var fragment = "<p>Café 🎉 naïve</p>";
        var bytes = CopyWithDescription.ClipboardHtml(fragment);
        Assert.Equal(0, bytes[^1]);
        var text = Encoding.UTF8.GetString(bytes, 0, bytes.Length - 1);
        int Offset(string name) => int.Parse(text.Split("\r\n").First(l => l.StartsWith(name + ":", StringComparison.Ordinal))[(name.Length + 1)..]);
        var (startHtml, endHtml) = (Offset("StartHTML"), Offset("EndHTML"));
        var (startFragment, endFragment) = (Offset("StartFragment"), Offset("EndFragment"));
        Assert.Equal(fragment, Encoding.UTF8.GetString(bytes, startFragment, endFragment - startFragment));
        Assert.StartsWith("<html>", Encoding.UTF8.GetString(bytes, startHtml, 6));
        Assert.Equal(bytes.Length - 1, endHtml);
        Assert.EndsWith("</html>", Encoding.UTF8.GetString(bytes, startHtml, endHtml - startHtml));
    }

    [Fact]
    public void DataUri_SaysWhichTypeOfPicture()
    {
        Assert.Equal("data:image/png;base64,AQID", CopyWithDescription.DataUri([1, 2, 3], ".png"));
        Assert.StartsWith("data:image/jpeg;base64,", CopyWithDescription.DataUri([1], ".jpg"));
    }

    [Fact]
    public void File_HasTheDescription_AndReplacesTheLastCopy()
    {
        var fields = Fields("A green square.", "GO");
        var screen = new Picture(Drawn.Solid(20, 10, 0, 200, 0), "Screen");
        var first = CopyWithDescription.SaveFile(screen, Taken, fields, _folder);
        Assert.Equal("Screen 2026-11-24 14.05.12.png", Path.GetFileName(first));
        var back = EmbeddedDescription.Fields(EmbeddedDescription.PacketIn(File.ReadAllBytes(first))!);
        Assert.Equal((fields.Caption, fields.Title), (back.Caption, back.Title));

        // The camera's picture is a JPEG. The screen's file stays until the newer one is on the clipboard, then goes.
        var camera = CopyWithDescription.SaveFile(new Picture(Drawn.Solid(20, 10, 0, 0, 200), "Camera"), Taken, fields, _folder);
        Assert.Equal(".jpg", Path.GetExtension(camera));
        Assert.Equal(2, Directory.GetFiles(_folder).Length);
        CopyWithDescription.RemoveOlder(_folder, camera);
        Assert.Equal([camera], Directory.GetFiles(_folder));
    }

    [Fact]
    public void File_StillInUse_IsKept_AndTheNewOneGetsItsOwnName()
    {
        var screen = new Picture(Drawn.Solid(8, 8, 0, 0, 0), "Screen");
        var first = CopyWithDescription.SaveFile(screen, Taken, null, _folder);
        var second = CopyWithDescription.SaveFile(screen, Taken, null, _folder);
        Assert.Equal("Screen 2026-11-24 14.05.12 (2).png", Path.GetFileName(second));
        using (File.Open(first, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            CopyWithDescription.RemoveOlder(_folder, second);
            Assert.Equal(2, Directory.GetFiles(_folder).Length);
        }
        CopyWithDescription.RemoveOlder(_folder, second);
        Assert.Equal([second], Directory.GetFiles(_folder));
        // A folder not made yet is nothing to remove.
        CopyWithDescription.RemoveOlder(Path.Combine(_folder, "none"), second);
    }

    [Fact]
    public void Prepared_HasTheTextAndTheHtmlPictureIsTheFile()
    {
        var fields = Fields("A green square.", "GO");
        var prepared = CopyWithDescription.Prepare(new Picture(Drawn.Solid(12, 12, 0, 200, 0), "Screen"), Taken, fields, _folder);
        Assert.Equal(fields.Caption, prepared.Text);
        var html = Encoding.UTF8.GetString(prepared.Html, 0, prepared.Html.Length - 1);
        Assert.Contains(CopyWithDescription.DataUri(File.ReadAllBytes(prepared.File), ".png"), html);
        Assert.Contains("<p>Text in the picture:<br>GO</p>", html);
        Assert.True(prepared.Png.Length > 0);

        var plain = CopyWithDescription.Prepare(new Picture(Drawn.Solid(12, 12, 0, 200, 0), "Screen"), Taken, null, _folder);
        Assert.Null(plain.Text);
        Assert.Null(EmbeddedDescription.PacketIn(File.ReadAllBytes(plain.File)));
        Assert.Contains("alt=\"Screen\"", Encoding.UTF8.GetString(plain.Html));

        var focus = CopyWithDescription.Prepare(new Picture(Drawn.Solid(4, 4, 0, 0, 0), "Focused item" + ScreenCapture.NoPlaceNote), Taken, null, _folder);
        Assert.Contains("alt=\"Focused item\"", Encoding.UTF8.GetString(focus.Html));
    }

    [Fact]
    public void F1_SaysCtrlShiftDCopiesBoth()
    {
        Assert.Contains("Ctrl+Shift+D copies the picture and its description", MainWindow.InAppKeys(false));
    }

    /// <summary>Puts a picture and its description on this PC's real clipboard and pastes it into a hidden Word document,
    /// which it closes. Only when QD_CLIPBOARD_TRIAL=1: it replaces what's on the clipboard, and needs Word.</summary>
    [Fact]
    public void PastesIntoWord_WithItsAltTextAndDescription()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("QD_CLIPBOARD_TRIAL") == "1", "Set QD_CLIPBOARD_TRIAL=1 to run.");
        var fields = Fields("A green square on white.", "GO");
        var prepared = CopyWithDescription.Prepare(new Picture(Drawn.Solid(60, 40, 0, 200, 0), "Screen"), Taken, fields, _folder);
        var (pictures, alt, text) = Drawn.OnSta(() =>
        {
            Clipboard.SetDataObject(CopyWithDescription.ClipboardData(prepared), true);
            dynamic word = Activator.CreateInstance(Type.GetTypeFromProgID("Word.Application")!)!;
            try
            {
                word.Visible = false;
                dynamic doc = word.Documents.Add();
                word.Selection.Paste();
                int count = doc.InlineShapes.Count;
                string altText = count > 0 ? doc.InlineShapes.Item(1).AlternativeText : "";
                string content = doc.Content.Text;
                doc.Close(0);
                return (count, altText, content);
            }
            finally
            {
                word.Quit(0);
            }
        });
        Assert.Equal(1, pictures);
        Assert.Equal(fields.AltText, alt);
        Assert.Contains("A green square on white.", text);
        Assert.Contains("GO", text);
    }
}
