using QuickDescribe.Services;

namespace QuickDescribe.Tests;

public class PixelRectTests
{
    private static readonly PixelRect Monitor = new(0, 0, 1920, 1080);

    [Fact]
    public void ASmallItem_GrowsToTheMinimum_CentredOnIt()
    {
        var area = PixelRect.AroundFocus(PixelRect.FromSize(900, 500, 80, 24), Monitor)!.Value;
        Assert.Equal(400, area.Width);
        Assert.Equal(200, area.Height);
        Assert.Equal(900 + 40, area.Left + area.Width / 2);
        Assert.Equal(500 + 12, area.Top + area.Height / 2);
    }

    [Fact]
    public void ALargeItem_GetsJustTheMargin()
    {
        var area = PixelRect.AroundFocus(PixelRect.FromSize(100, 100, 600, 400), Monitor)!.Value;
        Assert.Equal(new PixelRect(84, 84, 716, 516), area);
    }

    [Fact]
    public void AnItemInACorner_SlidesInsideTheMonitor_WithoutShrinking()
    {
        var area = PixelRect.AroundFocus(PixelRect.FromSize(0, 0, 50, 20), Monitor)!.Value;
        Assert.Equal(new PixelRect(0, 0, 400, 200), area);

        var bottomRight = PixelRect.AroundFocus(PixelRect.FromSize(1900, 1070, 20, 10), Monitor)!.Value;
        Assert.Equal(new PixelRect(1520, 880, 1920, 1080), bottomRight);
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
        Assert.Equal(400, area.Width);
        Assert.True(area.Left >= left.Left);
    }

    [Fact]
    public void AtDoubleScaling_TheAreaIsTwiceAsManyPixels()
    {
        var area = PixelRect.AroundFocus(PixelRect.FromSize(900, 500, 160, 48), Monitor,
            PixelRect.FocusMargin * 2, PixelRect.FocusMinWidth * 2, PixelRect.FocusMinHeight * 2)!.Value;
        Assert.Equal((800, 400), (area.Width, area.Height));
    }

    [Fact]
    public void Intersect_OfSeparateRectangles_IsEmpty()
    {
        Assert.True(new PixelRect(0, 0, 10, 10).Intersect(new PixelRect(20, 20, 30, 30)).IsEmpty);
        Assert.Equal(new PixelRect(5, 5, 10, 10), new PixelRect(0, 0, 10, 10).Intersect(new PixelRect(5, 5, 30, 30)));
    }
}
