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
}
