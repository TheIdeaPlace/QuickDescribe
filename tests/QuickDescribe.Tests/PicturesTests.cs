using System.Windows.Media.Imaging;
using QuickDescribe.Services;

namespace QuickDescribe.Tests;

public class PicturesTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("QuickDescribeTests").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    [Fact]
    public void SeeThroughPixels_AreFlattenedOntoWhite_NotBlack()
    {
        var (pixels, _, _) = Pictures.OpaquePixels(Drawn.Solid(2, 2, 0, 0, 0, alpha: 0));
        Assert.All(pixels, b => Assert.Equal(255, b));

        var (half, _, _) = Pictures.OpaquePixels(Drawn.Solid(1, 1, 0, 0, 255, alpha: 128));
        Assert.Equal(127, half[0]); // blue: none of its own, half of white's
        Assert.Equal(255, half[2]); // red stays full
        Assert.Equal(255, half[3]);
    }

    [Fact]
    public void OpaquePixels_LeavesOpaquePicturesAlone()
    {
        var (pixels, w, h) = Pictures.OpaquePixels(Drawn.Solid(3, 2, 10, 20, 30));
        Assert.Equal((3, 2), (w, h));
        Assert.Equal(new byte[] { 10, 20, 30, 255 }, pixels[..4]);
    }

    [Theory]
    [InlineData(1000, 500, 2048u, 1.0)]
    [InlineData(4096, 1024, 2048u, 0.5)]
    [InlineData(1000, 4000, 1000u, 0.25)]
    [InlineData(0, 0, 2048u, 1.0)]
    public void FitScale_OnlyEverShrinks(int width, int height, uint max, double expected) =>
        Assert.Equal(expected, Pictures.FitScale(width, height, max), 3);

    [Fact]
    public void Jpeg_IsAReadableJpeg_NoLargerThanAsked()
    {
        var jpeg = Pictures.Jpeg(Drawn.Solid(3000, 1500, 0, 0, 200), 1024);
        Assert.Equal(new byte[] { 0xFF, 0xD8 }, jpeg[..2]);
        var frame = BitmapDecoder.Create(new MemoryStream(jpeg), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        Assert.Equal(1024, frame.PixelWidth);
        Assert.Equal(512, frame.PixelHeight);
    }

    [Fact]
    public async Task Jpeg_WorksOffTheUIThread_AsDescribingUsesIt()
    {
        var picture = Drawn.Solid(500, 300, 0, 0, 200);
        var jpeg = await Task.Run(() => Pictures.Jpeg(picture, 2048));
        Assert.True(jpeg.Length > 100);
    }

    [Theory]
    [InlineData(1, 40, 20)]
    [InlineData(3, 40, 20)]
    [InlineData(6, 20, 40)]
    [InlineData(8, 20, 40)]
    [InlineData(5, 20, 40)]
    public void Oriented_TurnsSidewaysPhotosUpright(int orientation, int width, int height)
    {
        var turned = Pictures.Oriented(Drawn.Solid(40, 20, 0, 0, 0), orientation);
        Assert.Equal((width, height), (turned.PixelWidth, turned.PixelHeight));
    }

    [Fact]
    public void FromFile_ReadsAPicture_NamedByItsFile()
    {
        var path = Drawn.SavePng(Drawn.Solid(30, 20, 0, 255, 0), Path.Combine(_folder, "green.png"));
        var picture = Pictures.FromFile(path);
        Assert.Equal("green.png", picture.Source);
        Assert.Equal(30, picture.Image.PixelWidth);
        Assert.True(picture.Image.IsFrozen);
    }

    [Fact]
    public void FromFile_ThatIsntAPicture_SaysSoByName()
    {
        var path = Path.Combine(_folder, "notes.png");
        File.WriteAllText(path, "not a picture");
        var ex = Assert.Throws<PictureException>(() => Pictures.FromFile(path));
        Assert.StartsWith("notes.png couldn't be read as a picture", ex.Message);
    }

    [Fact]
    public void FromFile_ThatIsMissing_SaysSoByName()
    {
        var ex = Assert.Throws<PictureException>(() => Pictures.FromFile(Path.Combine(_folder, "gone.jpg")));
        Assert.StartsWith("gone.jpg couldn't be read", ex.Message);
    }

    [Fact]
    public void FirstPictureFile_SkipsOtherFiles_WhateverTheCase()
    {
        Assert.Equal(@"C:\b.JPG", Pictures.FirstPictureFile([@"C:\a.txt", @"C:\b.JPG", @"C:\c.png"]));
        Assert.Null(Pictures.FirstPictureFile([@"C:\a.txt", @"C:\folder"]));
    }

    [Fact]
    public void TheOpenFilter_ListsEveryType()
    {
        Assert.StartsWith("Pictures|*.jpg;*.jpeg;*.png", Pictures.OpenFilter);
        Assert.EndsWith("|All files|*.*", Pictures.OpenFilter);
    }

    // ---- Save Screenshot ----

    private static readonly DateTime Taken = new(2026, 10, 30, 14, 5, 12);

    [Theory]
    [InlineData("Screen", "Screen 2026-10-30 14.05.12.png")]
    [InlineData("Active window, Untitled - Notepad", "Active window, Untitled - Notepad 2026-10-30 14.05.12.png")]
    [InlineData(@"Focused item, C:\Users\a.txt: ""draft""?", "Focused item, C Users a.txt draft 2026-10-30 14.05.12.png")]
    [InlineData("Active window, Ends with a dot.", "Active window, Ends with a dot 2026-10-30 14.05.12.png")]
    [InlineData("Active window, Notepad (the focused item has no place on the screen)", "Active window, Notepad 2026-10-30 14.05.12.png")]
    [InlineData("<>:|", "Screenshot 2026-10-30 14.05.12.png")]
    [InlineData("", "Screenshot 2026-10-30 14.05.12.png")]
    public void ScreenshotName_SaysWhatAndWhen_AsAFileCanBeNamed(string source, string expected) =>
        Assert.Equal(expected, Pictures.ScreenshotName(source, Taken));

    [Fact]
    public void ScreenshotName_KeepsALongTitleShort()
    {
        var name = Pictures.ScreenshotName("Active window, " + new string('x', 300), Taken);
        Assert.Equal(60 + " 2026-10-30 14.05.12.png".Length, name.Length);
        Assert.Empty(name.Intersect(Path.GetInvalidFileNameChars()));
    }

    [Fact]
    public void ScreenshotName_DoesntCutAnEmojiInHalf()
    {
        // 59 characters, then an emoji (two UTF-16 halves) across the 60-character limit.
        var name = Pictures.ScreenshotName(new string('x', 59) + "\U0001F600 tail", Taken);
        Assert.Equal(new string('x', 59) + " 2026-10-30 14.05.12.png", name);
    }

    [Fact]
    public void SaveProblem_NamesTheChosenFile_NotThePartOne()
    {
        const string path = @"D:\x\shot.png", partial = path + ".partial";
        Assert.StartsWith("Windows doesn't allow", Pictures.SaveProblem(new UnauthorizedAccessException($"Access to the path '{partial}' is denied."), partial, path));
        Assert.Equal("the folder isn't there any more.", Pictures.SaveProblem(new DirectoryNotFoundException(partial), partial, path));
        Assert.Equal("the drive is full.", Pictures.SaveProblem(new IOException("full", unchecked((int)0x80070070)), partial, path));
        Assert.Equal($"The process cannot access the file '{path}'.", Pictures.SaveProblem(new IOException($"The process cannot access the file '{partial}'."), partial, path));
    }

    [Theory]
    [InlineData("shot.png", new byte[] { 0x89, 0x50, 0x4E, 0x47 })]
    [InlineData("shot.PNG", new byte[] { 0x89, 0x50, 0x4E, 0x47 })]
    [InlineData("shot.jpg", new byte[] { 0xFF, 0xD8 })]
    [InlineData("shot.jpeg", new byte[] { 0xFF, 0xD8 })]
    [InlineData("shot.bmp", new byte[] { 0x89, 0x50, 0x4E, 0x47 })]
    public void Save_WritesTheTypeItsExtensionSays_AtFullSize(string name, byte[] start)
    {
        var path = Path.Combine(_folder, name);
        Pictures.Save(Drawn.Solid(300, 200, 0, 0, 200), path);
        Assert.Equal(start, File.ReadAllBytes(path)[..start.Length]);
        var read = Pictures.FromFile(path);
        Assert.Equal((300, 200), (read.Image.PixelWidth, read.Image.PixelHeight));
        Assert.False(File.Exists(path + ".partial"));
    }

    [Fact]
    public void Save_ReplacesAFileThatsThere()
    {
        var path = Path.Combine(_folder, "again.png");
        File.WriteAllText(path, "old");
        Pictures.Save(Drawn.Solid(10, 10, 0, 0, 0), path);
        Assert.Equal(10, Pictures.FromFile(path).Image.PixelWidth);
    }

    [Fact]
    public async Task Save_WorksOffTheUIThread_AsSaveScreenshotUsesIt()
    {
        var picture = Drawn.Solid(400, 300, 0, 200, 0);
        var path = Path.Combine(_folder, "off.png");
        await Task.Run(() => Pictures.Save(picture, path), TestContext.Current.CancellationToken);
        Assert.True(new FileInfo(path).Length > 0);
    }

    [Fact]
    public void Save_ToAFolderThatIsntThere_SaysSoByName_AndLeavesNothing()
    {
        var path = Path.Combine(_folder, "gone", "shot.png");
        var ex = Assert.Throws<PictureException>(() => Pictures.Save(Drawn.Solid(10, 10, 0, 0, 0), path));
        Assert.Equal("shot.png couldn't be saved: the folder isn't there any more.", ex.Message);
        Assert.False(File.Exists(path + ".partial"));
    }

    [Fact]
    public void Save_OverAFileInUse_SaysSo_AndLeavesTheFileAlone()
    {
        var path = Path.Combine(_folder, "busy.png");
        File.WriteAllText(path, "old");
        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var ex = Assert.Throws<PictureException>(() => Pictures.Save(Drawn.Solid(10, 10, 0, 0, 0), path));
            Assert.StartsWith("busy.png couldn't be saved:", ex.Message);
            Assert.DoesNotContain(".partial", ex.Message);
        }
        Assert.Equal("old", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".partial"));
    }

    [Fact]
    public void TheSaveFilter_OffersPngFirst()
    {
        Assert.StartsWith("PNG picture|*.png|", Pictures.SaveFilter);
        Assert.Contains("*.jpg", Pictures.SaveFilter);
    }
}
