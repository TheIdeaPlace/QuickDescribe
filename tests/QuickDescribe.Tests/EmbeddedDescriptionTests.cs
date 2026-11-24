using System.Windows.Media.Imaging;
using QuickDescribe.Services;

namespace QuickDescribe.Tests;

public class EmbeddedDescriptionTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("QuickDescribeTests").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    private static readonly DateTime Taken = new(2026, 11, 24, 14, 5, 12);

    private static Result Described(string? description, params string[] lines) =>
        new(description, description is null ? "too_much_text" : null, null, lines.Length > 0 ? lines : null, null);

    private static readonly string Gap = Environment.NewLine + Environment.NewLine;

    [Fact]
    public void Caption_IsTheDescriptionThenTheText_AndAltTextTheDescription()
    {
        var fields = EmbeddedDescription.For(Described("A red mug on a desk.", "COFFEE", "Mon 9:00"))!;
        Assert.Equal($"A red mug on a desk.{Gap}Text in the picture:{Environment.NewLine}COFFEE{Environment.NewLine}Mon 9:00", fields.Caption);
        Assert.Equal("A red mug on a desk.", fields.AltText);
        Assert.Equal("A red mug on a desk.", fields.Title);
        // The alt text is less than the caption, so the whole of it is the extended description.
        Assert.Equal(fields.Caption, fields.ExtendedDescription);
    }

    [Fact]
    public void ADescriptionAlone_NeedsNoExtendedDescription()
    {
        var fields = EmbeddedDescription.For(Described("A red mug on a desk."))!;
        Assert.Equal("A red mug on a desk.", fields.Caption);
        Assert.Null(fields.ExtendedDescription);
    }

    [Fact]
    public void ScreenWithOnlyText_HasTheTextButNoTitle_AndNoProblemLines()
    {
        var fields = EmbeddedDescription.For(Described(null, "Inbox", "3 unread"))!;
        Assert.Equal($"Text in the picture:{Environment.NewLine}Inbox{Environment.NewLine}3 unread", fields.Caption);
        Assert.DoesNotContain("No description", fields.Caption);
        Assert.Equal($"Inbox{Environment.NewLine}3 unread", fields.AltText);
        Assert.Null(fields.Title);
    }

    [Fact]
    public void NothingToPut_IsNull()
    {
        Assert.Null(EmbeddedDescription.For(Described(null)));
        Assert.Null(EmbeddedDescription.For(new Result("  ", null, null, ["", " "], null)));
    }

    [Fact]
    public void LongDescription_AltTextKeepsWholeSentencesWithin250()
    {
        var sentence = "A wooden table holds a stack of books, a lamp and a cup of tea beside an open window. ";
        var description = string.Concat(Enumerable.Repeat(sentence, 5)).Trim();
        var fields = EmbeddedDescription.For(Described(description))!;
        Assert.True(fields.AltText.Length <= EmbeddedDescription.AltTextLimit);
        Assert.EndsWith(".", fields.AltText);
        Assert.Equal(description, fields.ExtendedDescription);
    }

    [Fact]
    public void OneLongSentence_IsCutAtAWord()
    {
        var alt = EmbeddedDescription.Shorten(string.Join(' ', Enumerable.Repeat("word", 100)));
        Assert.True(alt.Length <= EmbeddedDescription.AltTextLimit);
        Assert.EndsWith("word…", alt);
    }

    [Theory]
    [InlineData(".png")]
    [InlineData(".jpg")]
    public void SavedPicture_HoldsTheFields_AndStillOpens(string extension)
    {
        var image = Drawn.Solid(40, 30, 10, 20, 30);
        var fields = EmbeddedDescription.For(Described("A café sign: “Open”.", "Line one", "Line & <two>"))!;
        var path = Path.Combine(_folder, "described" + extension);
        Pictures.Save(image, path, fields);

        var bytes = File.ReadAllBytes(path);
        var back = EmbeddedDescription.Fields(EmbeddedDescription.PacketIn(bytes)!);
        Assert.Equal(fields.Caption, back.Caption);
        Assert.Equal(fields.AltText, back.AltText);
        Assert.Equal(fields.ExtendedDescription, back.ExtendedDescription);
        Assert.Equal(fields.Title, back.Title);
        Assert.Equal(fields.Caption, back.Comment);
        AssertOpens(path, 40, 30);
    }

    [Fact]
    public void Jpeg_TitleIsWhatWindowsReads()
    {
        var path = Path.Combine(_folder, "camera.jpg");
        Pictures.Save(Drawn.Solid(20, 20, 0, 0, 0), path, EmbeddedDescription.For(Described("A cat on a sofa.")));
        // Through WIC's photo metadata policy, as File Explorer's Title is read.
        var title = Drawn.OnSta(() =>
        {
            using var stream = File.OpenRead(path);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            var metadata = (BitmapMetadata)frame.Metadata;
            return (metadata.Title, metadata.Comment);
        });
        Assert.Equal(("A cat on a sofa.", "A cat on a sofa."), title);
    }

    [Fact]
    public void Png_TitleIsWhatWindowsReads_AndItsChunkChecksOut()
    {
        var path = Path.Combine(_folder, "screen.png");
        Pictures.Save(Drawn.Solid(20, 20, 0, 0, 0), path, EmbeddedDescription.For(Described("A cat on a sofa.")));
        var title = Drawn.OnSta(() =>
        {
            using var stream = File.OpenRead(path);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            var metadata = (BitmapMetadata)frame.Metadata;
            return (metadata.Title, metadata.Comment);
        });
        Assert.Equal(("A cat on a sofa.", "A cat on a sofa."), title);

        // Every chunk's CRC is right: a decoder may skip a bad one quietly, and so lose the fields.
        Assert.Equal(0xAE426082u, EmbeddedDescription.Crc32("IEND"u8));
        var png = File.ReadAllBytes(path);
        var chunks = 0;
        for (var at = 8; at < png.Length; chunks++)
        {
            var length = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at));
            Assert.Equal(System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 8 + length)),
                EmbeddedDescription.Crc32(png.AsSpan(at + 4, 4 + length)));
            at += 12 + length;
        }
        Assert.True(chunks >= 4);
    }

    [Fact]
    public void CharactersXmlCantHold_AreLeftOut_AndThePictureIsSaved()
    {
        var fields = EmbeddedDescription.For(Described("A bell\u0001 rings.", "Half \ud83d an emoji", "Whole 😀 one"))!;
        Assert.Equal("A bell rings.", fields.Description);
        Assert.Contains("Half  an emoji", fields.Caption);
        Assert.Contains("Whole 😀 one", fields.Caption);
        var path = Path.Combine(_folder, "odd.png");
        Pictures.Save(Drawn.Solid(10, 10, 0, 0, 0), path, fields);
        Assert.Equal(fields.Caption, EmbeddedDescription.Fields(EmbeddedDescription.PacketIn(File.ReadAllBytes(path))!).Caption);
    }

    [Fact]
    public void Jpeg_TooMuchTextAlone_StillGetsItsAltText()
    {
        var lines = Enumerable.Range(0, 3000).Select(i => $"Line {i} of a very long page of text").ToArray();
        var fields = EmbeddedDescription.For(Described(null, lines))!;
        var path = Path.Combine(_folder, "page.jpg");
        Pictures.Save(Drawn.Solid(10, 10, 0, 0, 0), path, fields);
        var back = EmbeddedDescription.Fields(EmbeddedDescription.PacketIn(File.ReadAllBytes(path))!);
        Assert.Equal(fields.AltText, back.AltText);
        Assert.StartsWith(Result.TextHeading, back.Caption);
    }

    [Fact]
    public async Task AddToFile_WaitsOutAFileHeldOpen()
    {
        var path = Pictures.SaveToFolder(new Picture(Drawn.Solid(8, 8, 0, 0, 0), "Screen"), Taken, _folder);
        var fields = EmbeddedDescription.For(Described("Black."))!;
        // Held open the way OneDrive or the indexer would, for a moment, as the fields are added.
        var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = Task.Delay(300, TestContext.Current.CancellationToken).ContinueWith(_ => held.Dispose(), TestContext.Current.CancellationToken);
        await Task.Run(() => fields.AddToFile(path, [200, 400, 800]), TestContext.Current.CancellationToken);
        await release;
        Assert.Equal("Black.", EmbeddedDescription.Fields(EmbeddedDescription.PacketIn(File.ReadAllBytes(path))!).Caption);
        Assert.Empty(Directory.GetFiles(_folder, "*.partial"));
    }

    [Fact]
    public void AddToFile_HeldOpenThroughout_FailsAndLeavesNothingBehind()
    {
        var path = Pictures.SaveToFolder(new Picture(Drawn.Solid(8, 8, 0, 0, 0), "Screen"), Taken, _folder);
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.ThrowsAny<UnauthorizedAccessException>(() => EmbeddedDescription.For(Described("Black."))!.AddToFile(path, [50]));
        Assert.Null(EmbeddedDescription.PacketIn(File.ReadAllBytes(path)));
        Assert.Empty(Directory.GetFiles(_folder, "*.partial"));
    }

    [Fact]
    public void Leftovers_AreRemoved_AndPicturesKept()
    {
        var picture = Pictures.SaveToFolder(new Picture(Drawn.Solid(8, 8, 0, 0, 0), "Screen"), Taken, _folder);
        File.WriteAllText(picture + ".0123456789abcdef0123456789abcdef.partial", "cut short");
        EmbeddedDescription.RemoveLeftovers(_folder);
        Assert.Equal([picture], Directory.GetFiles(_folder));
        EmbeddedDescription.RemoveLeftovers(Path.Combine(_folder, "not there"));
    }

    [Fact]
    public void Jpeg_XmpGoesAfterJfif()
    {
        var path = Path.Combine(_folder, "order.jpg");
        Pictures.Save(Drawn.Solid(10, 10, 0, 0, 0), path, EmbeddedDescription.For(Described("Dark.")));
        var jpeg = File.ReadAllBytes(path);
        var markers = new List<byte>();
        for (var at = 2; jpeg[at] == 0xFF && jpeg[at + 1] != 0xDA; at += 2 + System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(at + 2)))
            markers.Add(jpeg[at + 1]);
        Assert.Equal(0xE0, markers[0]); // JFIF first, as WPF wrote it
        Assert.Equal(0xE1, markers[1]); // then the XMP
    }

    [Fact]
    public void Shorten_NeverCutsAnEmojiInHalf()
    {
        // The 249th and 250th characters are the two halves of one emoji.
        var text = new string('a', 248) + "😀" + " and more words after it";
        var alt = EmbeddedDescription.Shorten(text);
        Assert.True(alt.Length <= EmbeddedDescription.AltTextLimit);
        Assert.False(char.IsHighSurrogate(alt[^2]) && !char.IsLowSurrogate(alt[^1]));
        Assert.All(alt.Select((c, i) => (c, i)).Where(x => char.IsHighSurrogate(x.c)), x => Assert.True(char.IsLowSurrogate(alt[x.i + 1])));
    }

    [Fact]
    public void Comment_HoldsTheWholeCaption_LongerThanExplorerShowsATitle()
    {
        var description = string.Concat(Enumerable.Repeat("A sentence about the picture that goes on for a while. ", 15)).Trim();
        var fields = EmbeddedDescription.For(Described(description, "Some text"))!;
        foreach (var name in new[] { "long.png", "long.jpg" })
        {
            var path = Path.Combine(_folder, name);
            Pictures.Save(Drawn.Solid(10, 10, 0, 0, 0), path, fields);
            var comment = Drawn.OnSta(() =>
            {
                using var stream = File.OpenRead(path);
                return ((BitmapMetadata)BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0].Metadata).Comment;
            });
            // Windows reads the line breaks back as LF alone.
            Assert.Equal(fields.Caption.ReplaceLineEndings("\n"), comment);
        }
    }

    [Theory]
    // Two short sentences fit in what Explorer shows.
    [InlineData("A cat on a sofa. It is asleep. A lamp is beside it.", "A cat on a sofa. It is asleep.")]
    // Windows' descriptions of the 24 Nov 2026 screens: two sentences are too long together, so the first alone.
    [InlineData("The image appears to be a user interface, likely from a software application or operating system. It shows a black background with a blue button labeled \"Getting Started\" at the center. Below it, there are various options.",
        "The image appears to be a user interface, likely from a software application or operating system.")]
    [InlineData("One sentence without an end", "One sentence without an end")]
    public void Title_IsOneOrTwoSentences(string description, string title)
    {
        Assert.Equal(title, EmbeddedDescription.ShortTitle(description));
        Assert.Equal(title, EmbeddedDescription.For(Described(description))!.Title);
    }

    [Fact]
    public void Title_OneVeryLongSentence_IsShortenedLikeAltText()
    {
        var sentence = string.Join(' ', Enumerable.Repeat("word", 80)) + ".";
        Assert.Equal(EmbeddedDescription.Shorten(sentence), EmbeddedDescription.ShortTitle(sentence));
    }

    [Fact]
    public void WithoutFields_NothingIsAdded()
    {
        var path = Path.Combine(_folder, "plain.png");
        Pictures.Save(Drawn.Solid(10, 10, 0, 0, 0), path);
        Assert.Null(EmbeddedDescription.PacketIn(File.ReadAllBytes(path)));
    }

    [Fact]
    public void SaveEverythingCopy_GetsTheFieldsAfterwards()
    {
        var image = Drawn.Solid(16, 16, 200, 100, 50);
        foreach (var source in new[] { "Screen", "Camera" })
        {
            var path = Pictures.SaveToFolder(new Picture(image, source), Taken, _folder);
            var fields = EmbeddedDescription.For(Described("Orange.", "TEXT"))!;
            fields.AddToFile(path);
            Assert.Equal(fields.Caption, EmbeddedDescription.Fields(EmbeddedDescription.PacketIn(File.ReadAllBytes(path))!).Caption);
            Assert.Empty(Directory.GetFiles(_folder, "*.partial"));
            AssertOpens(path, 16, 16);
            // A second time is refused, leaving the file as it was.
            var before = File.ReadAllBytes(path);
            Assert.Throws<InvalidDataException>(() => fields.AddToFile(path));
            Assert.Equal(before, File.ReadAllBytes(path));
        }
    }

    [Fact]
    public void Jpeg_TooMuchText_KeepsTheDescription()
    {
        var lines = Enumerable.Range(0, 3000).Select(i => $"Line {i} of a very long page of text").ToArray();
        var fields = EmbeddedDescription.For(Described("A printed page.", lines))!;
        var path = Path.Combine(_folder, "long.jpg");
        Pictures.Save(Drawn.Solid(10, 10, 0, 0, 0), path, fields);
        var back = EmbeddedDescription.Fields(EmbeddedDescription.PacketIn(File.ReadAllBytes(path))!);
        Assert.Equal("A printed page.", back.Caption);
        AssertOpens(path, 10, 10);

        // A PNG has room for it all.
        var png = Path.Combine(_folder, "long.png");
        Pictures.Save(Drawn.Solid(10, 10, 0, 0, 0), png, fields);
        Assert.Equal(fields.Caption, EmbeddedDescription.Fields(EmbeddedDescription.PacketIn(File.ReadAllBytes(png))!).Caption);
    }

    [Fact]
    public void Setting_IsOnByDefault_AlsoForOlderSettings()
    {
        Assert.True(new Settings().AttachDescriptions);
        var path = Path.Combine(_folder, "old.json");
        File.WriteAllText(path, """{ "SaveEverything": true }""");
        Assert.True(Settings.Load(path).AttachDescriptions);
        var off = new Settings { AttachDescriptions = false };
        off.Save(path);
        Assert.False(Settings.Load(path).AttachDescriptions);
    }

    private static void AssertOpens(string path, int width, int height)
    {
        var size = Drawn.OnSta(() =>
        {
            using var stream = File.OpenRead(path);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            return (frame.PixelWidth, frame.PixelHeight);
        });
        Assert.Equal((width, height), size);
    }
}
