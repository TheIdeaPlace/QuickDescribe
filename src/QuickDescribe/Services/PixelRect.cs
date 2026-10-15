namespace QuickDescribe.Services;

/// <summary>A rectangle in physical screen pixels. Right and Bottom are exclusive, as Win32's RECT.</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static PixelRect FromSize(int left, int top, int width, int height) => new(left, top, left + width, top + height);

    public PixelRect Intersect(PixelRect other)
    {
        var r = new PixelRect(Math.Max(Left, other.Left), Math.Max(Top, other.Top), Math.Min(Right, other.Right), Math.Min(Bottom, other.Bottom));
        return r.IsEmpty ? default : r;
    }

    /// <summary>The margin and smallest area around a focused item, in pixels at 100% scaling: enough to take in its label
    /// and what's beside it, without so much that the description is about something else.</summary>
    public const int FocusMargin = 16, FocusMinWidth = 400, FocusMinHeight = 200;

    /// <summary>
    /// The area to capture around a focused item: the item with a margin, grown to at least the minimum size so the
    /// model sees enough to say what it is, kept inside <paramref name="bounds"/> (its monitor) by sliding rather than
    /// cutting where it can. Null when the item isn't on that monitor at all.
    /// </summary>
    public static PixelRect? AroundFocus(PixelRect item, PixelRect bounds, int margin = FocusMargin, int minWidth = FocusMinWidth,
        int minHeight = FocusMinHeight)
    {
        var visible = item.Intersect(bounds);
        if (visible.IsEmpty) return null;

        var left = visible.Left - margin;
        var top = visible.Top - margin;
        var right = visible.Right + margin;
        var bottom = visible.Bottom + margin;
        (left, right) = Grow(left, right, minWidth);
        (top, bottom) = Grow(top, bottom, minHeight);
        (left, right) = Fit(left, right, bounds.Left, bounds.Right);
        (top, bottom) = Fit(top, bottom, bounds.Top, bounds.Bottom);
        return new PixelRect(left, top, right, bottom);
    }

    /// <summary>Grows [start, end) evenly on both sides to at least <paramref name="min"/> long.</summary>
    private static (int, int) Grow(int start, int end, int min)
    {
        var shortBy = min - (end - start);
        if (shortBy <= 0) return (start, end);
        var before = shortBy / 2;
        return (start - before, end + shortBy - before);
    }

    /// <summary>Slides [start, end) inside [low, high), and cuts it to fit only when it's longer than that.</summary>
    private static (int, int) Fit(int start, int end, int low, int high)
    {
        var length = Math.Min(end - start, high - low);
        if (start < low) start = low;
        if (start + length > high) start = high - length;
        return (start, start + length);
    }

    public override string ToString() => $"{Width}×{Height} at {Left},{Top}";
}
