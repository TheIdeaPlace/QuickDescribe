using QuickDescribe.Services;

namespace QuickDescribe.Tests;

public class CopyOpenedPicturesTests
{
    [Fact]
    public void Setting_IsOnByDefault_AlsoForOlderSettings()
    {
        Assert.True(new Settings().CopyOpenedPictures);
        var folder = Directory.CreateTempSubdirectory("QuickDescribeTests").FullName;
        try
        {
            var path = Path.Combine(folder, "old.json");
            File.WriteAllText(path, """{ "SaveEverything": true }""");
            Assert.True(Settings.Load(path).CopyOpenedPictures);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Theory]
    [InlineData("holiday.jpg", ".jpg")]
    [InlineData("holiday.JPEG", ".jpg")]
    [InlineData("phone photo.heic", ".jpg")]
    [InlineData("web.webp", ".jpg")]
    [InlineData("diagram.png", ".png")]
    [InlineData("scan.tif", ".png")]
    [InlineData("old.bmp", ".png")]
    public void OpenedPhotos_StayJpeg_TheRestPng(string name, string extension)
    {
        Assert.Equal(extension, Pictures.ExtensionFor(name, isFile: true));
        Assert.Equal(extension == ".jpg" ? 2 : 1, Pictures.SaveFilterIndexFor(new Picture(Drawn.Solid(1, 1, 0, 0, 0), name, IsFile: true)));
    }

    [Fact]
    public void AWindowTitledLikeAFile_IsStillAScreenshot()
    {
        Assert.Equal(".png", Pictures.ExtensionFor("Active window, holiday.jpg - Photos"));
        Assert.Equal(".png", Pictures.ExtensionFor("holiday.jpg"));
    }

    [Fact]
    public void PicturesAlreadyInTheFolder_AreNotCopiedAgain()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QD Pictures", "Quick Describe");
        Assert.True(Pictures.IsInSaveEverythingFolder(Path.Combine(folder, "Screen 2026-11-24 05.57.40.png"), folder));
        Assert.True(Pictures.IsInSaveEverythingFolder(Path.Combine(folder, "older", "a.png"), folder));
        Assert.False(Pictures.IsInSaveEverythingFolder(Path.Combine(Path.GetTempPath(), "QD Pictures", "holiday.jpg"), folder));
        // A folder whose name only starts the same isn't it.
        Assert.False(Pictures.IsInSaveEverythingFolder(Path.Combine(folder + " old", "a.png"), folder));
        Assert.False(Pictures.IsInSaveEverythingFolder(null, folder));
    }

    [Fact]
    public void AnOpenedPicture_KnowsItsPath()
    {
        var folder = Directory.CreateTempSubdirectory("QuickDescribeTests").FullName;
        try
        {
            var path = Path.Combine(folder, "photo.png");
            Pictures.Save(Drawn.Solid(4, 4, 0, 0, 0), path);
            var picture = Pictures.FromFile(path);
            Assert.True(picture.IsFile);
            Assert.Equal(path, picture.FilePath);
            Assert.Equal("photo.png", picture.Source);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
