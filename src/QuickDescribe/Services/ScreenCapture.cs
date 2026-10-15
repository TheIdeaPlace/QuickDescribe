using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuickDescribe.Services;

/// <summary>A picture to describe, and what it is, as said to the person ("Active window, Notepad").</summary>
public sealed record Picture(BitmapSource Image, string Source);

/// <summary>Takes pictures of the screen: the monitor in use, the active window, or the area around the focused item.</summary>
public static class ScreenCapture
{
    /// <summary>The whole monitor the window is on.</summary>
    public static Picture Screen(IntPtr foreground)
    {
        var bounds = Native.MonitorOf(foreground);
        return new Picture(Capture(bounds), "Screen");
    }

    /// <summary>The window, as it's seen, cut to its monitor. Null with a reason when there's no window to take.</summary>
    public static (Picture? Picture, string? Problem) Window(IntPtr window)
    {
        if (window == IntPtr.Zero || !Native.IsWindow(window)) return (null, "There's no active window.");
        if (Native.IsIconic(window)) return (null, "The active window is minimized.");
        var bounds = Native.VisibleBounds(window);
        var visible = bounds?.Intersect(Native.MonitorOf(window)) ?? default;
        if (visible.IsEmpty) return (null, "The active window isn't on the screen.");
        var title = Native.WindowTitle(window).Trim();
        return (new Picture(Capture(visible), title.Length > 0 ? $"Active window, {title}" : "Active window"), null);
    }

    /// <summary>
    /// The area around the item with keyboard focus. When the item has no place on the screen (some controls don't
    /// say), the active window instead, and the source says so. On the UI thread: the slow part, asking UI Automation,
    /// runs on another.
    /// </summary>
    public static async Task<(Picture? Picture, string? Problem)> FocusAsync(IntPtr foreground)
    {
        var (area, name) = await Task.Run(FindFocus);
        if (area is { } a) return (new Picture(Capture(a), name.Length > 0 ? $"Focused item, {name}" : "Focused item"), null);
        var (window, problem) = Window(foreground);
        if (window is null) return (null, problem ?? "Couldn't find the focused item.");
        return (window with { Source = $"{window.Source} (the focused item has no place on the screen)" }, null);
    }

    /// <summary>The area to take around the focused item, and its name, from UI Automation. Not on the UI thread:
    /// UI Automation can wait on the app that has focus, which may be this one.</summary>
    private static (PixelRect? Area, string Name) FindFocus()
    {
        string name = "";
        Rect bounds = Rect.Empty;
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is not null)
            {
                name = Describe(focused);
                bounds = focused.Current.BoundingRectangle;
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // Focus moved, or the app with focus isn't answering: fall back to the window.
        }

        if (!bounds.IsEmpty && double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height))
        {
            var item = new PixelRect((int)Math.Floor(bounds.Left), (int)Math.Floor(bounds.Top),
                (int)Math.Ceiling(bounds.Right), (int)Math.Ceiling(bounds.Bottom));
            var (x, y) = (item.Left + item.Width / 2, item.Top + item.Height / 2);
            // The area around the item is measured in pixels at 100%, so it takes in as much of the screen at any scaling.
            var scale = Native.ScaleAt(x, y);
            return (PixelRect.AroundFocus(item, Native.MonitorAt(x, y), (int)(PixelRect.FocusMargin * scale),
                (int)(PixelRect.FocusMinWidth * scale), (int)(PixelRect.FocusMinHeight * scale)), name);
        }
        return (null, name);
    }

    /// <summary>"OK button": the item's name and its type, as a screen reader would say them.</summary>
    private static string Describe(AutomationElement element)
    {
        var current = element.Current;
        var name = (current.Name ?? "").Trim();
        var type = (current.LocalizedControlType ?? "").Trim();
        if (name.Length > 80) name = name[..80].TrimEnd() + "…";
        return string.Join(" ", new[] { name, type }.Where(s => s.Length > 0));
    }

    /// <summary>The screen's pixels in <paramref name="area"/>, as a frozen 32-bit picture any thread can use.</summary>
    public static BitmapSource Capture(PixelRect area)
    {
        if (area.IsEmpty) throw new ArgumentException("Nothing to capture.", nameof(area));
        var screen = Native.GetDC(IntPtr.Zero);
        var memory = Native.CreateCompatibleDC(screen);
        var bitmap = Native.CreateCompatibleBitmap(screen, area.Width, area.Height);
        var previous = Native.SelectObject(memory, bitmap);
        try
        {
            // CAPTUREBLT includes layered windows (tooltips, some menus), as people see them.
            if (!Native.BitBlt(memory, 0, 0, area.Width, area.Height, screen, area.Left, area.Top, Native.SRCCOPY | Native.CAPTUREBLT))
                throw new InvalidOperationException("Windows didn't give the screen's picture.");
            Native.SelectObject(memory, previous);
            previous = IntPtr.Zero;
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            // The screen has no transparency: Bgr32 says so, so nothing later treats its unused byte as alpha.
            var opaque = new FormatConvertedBitmap(source, PixelFormats.Bgr32, null, 0);
            var copy = new WriteableBitmap(opaque);
            copy.Freeze();
            return copy;
        }
        finally
        {
            if (previous != IntPtr.Zero) Native.SelectObject(memory, previous);
            Native.DeleteObject(bitmap);
            Native.DeleteDC(memory);
            Native.ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
