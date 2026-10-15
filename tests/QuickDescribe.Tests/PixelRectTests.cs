using QuickDescribe.Services;

namespace QuickDescribe.Tests;

public class PixelRectTests
{
    private static readonly PixelRect Monitor = new(0, 0, 1920, 1080);

    [Fact]
    public void ASmallItem_GrowsToTheMinimum_CentredOnIt()
    {
        var area = PixelRect.AroundFocus(PixelRect.FromSize(900, 500, 80, 24), Monitor)!.Value;
        Assert.Equal(320, area.Width);
        Assert.Equal(180, area.Height);
        Assert.Equal(900 + 40, area.Left + area.Width / 2);
        Assert.Equal(500 + 12, area.Top + area.Height / 2);
    }

    [Fact]
    public void ALargeItem_GetsJustTheMargin()
    {
        var area = PixelRect.AroundFocus(PixelRect.FromSize(100, 100, 600, 400), Monitor)!.Value;
        Assert.Equal(new PixelRect(88, 88, 712, 512), area);
    }

    [Fact]
    public void AnItemInACorner_SlidesInsideTheMonitor_WithoutShrinking()
    {
        var area = PixelRect.AroundFocus(PixelRect.FromSize(0, 0, 50, 20), Monitor)!.Value;
        Assert.Equal(new PixelRect(0, 0, 320, 180), area);

        var bottomRight = PixelRect.AroundFocus(PixelRect.FromSize(1900, 1070, 20, 10), Monitor)!.Value;
        Assert.Equal(new PixelRect(1600, 900, 1920, 1080), bottomRight);
    }

    [Fact]
    public void AnItemBiggerThanTheMonitor_IsCutToIt()
    {
        var area = PixelRect.AroundFocus(new PixelRect(-500, -500, 3000, 2000), Monitor)!.Value;
        Assert.Equal(Monitor, area);
    }

    [Fact]
    public void AnItemOffTheMonitor_HasNoArea()
    {
        Assert.Null(PixelRect.AroundFocus(PixelRect.FromSize(5000, 5000, 10, 10), Monitor));
        Assert.Null(PixelRect.AroundFocus(default, Monitor));
    }

    [Fact]
    public void ASecondMonitor_ToTheLeft_WithNegativeCoordinates_Works()
    {
        var left = new PixelRect(-1920, 0, 0, 1080);
        var area = PixelRect.AroundFocus(PixelRect.FromSize(-10, 10, 10, 10), left)!.Value;
        Assert.Equal(0, area.Right);
        Assert.Equal(320, area.Width);
        Assert.True(area.Left >= left.Left);
    }

    [Fact]
    public void Intersect_OfSeparateRectangles_IsEmpty()
    {
        Assert.True(new PixelRect(0, 0, 10, 10).Intersect(new PixelRect(20, 20, 30, 30)).IsEmpty);
        Assert.Equal(new PixelRect(5, 5, 10, 10), new PixelRect(0, 0, 10, 10).Intersect(new PixelRect(5, 5, 30, 30)));
    }
}
