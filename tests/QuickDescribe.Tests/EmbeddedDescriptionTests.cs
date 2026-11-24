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
            return ((BitmapMetadata)frame.Metadata).Title;
        });
        Assert.Equal("A cat on a sofa.", title);
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
            return ((BitmapMetadata)frame.Metadata).Title;
        });
        Assert.Equal("A cat on a sofa.", title);

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
