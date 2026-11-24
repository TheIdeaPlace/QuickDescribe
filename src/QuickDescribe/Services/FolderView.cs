using System.Diagnostics;
using System.Runtime.InteropServices;

namespace QuickDescribe.Services;

/// <summary>
/// File > Open Pictures Folder: opens Pictures\Quick Describe in File Explorer and, the first time, sets that window to
/// Details view with the columns that show what Quick Describe put in its pictures (<see cref="Columns"/>). Explorer
/// keeps a folder's view in the person's own settings, not in the folder, and a packaged app can't define a folder type
/// (that's HKLM), so the view is set on the open window through the shell's own interfaces (IShellBrowser, IFolderView,
/// IColumnManager), and Explorer remembers it as it does any change made there: checked on Kelly's PC, 24 Nov 2026,
/// closing and reopening the window kept Details view and exactly these columns. Set once (Settings.FolderViewSet), so a
/// view the person changes afterwards is theirs.
/// </summary>
public static class FolderView
{
    /// <summary>The columns, in order: what the picture is, when, its Title (one or two sentences), its Comments (the
    /// whole description and the text read) and its size. Type is left out: the names end in .png or .jpg.</summary>
    public static readonly (string Name, PropertyKey Key)[] Columns =
    [
        ("Name", new(new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 10)),          // System.ItemNameDisplay
        ("Date modified", new(new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 14)), // System.DateModified
        ("Title", new(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2)),          // System.Title
        ("Comments", new(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 6)),       // System.Comment
        ("Size", new(new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 12)),          // System.Size
    ];

    private const uint DetailsView = 4; // FVM_DETAILS
    private const uint VisibleColumns = 2; // CM_ENUM_VISIBLE

    /// <summary>Opens the folder in File Explorer (making it if it isn't there yet). With <paramref name="setView"/>,
    /// waits for the window, up to 10 seconds, and sets its view; returns whether that worked. Throws when Explorer
    /// can't be started. Call on the UI thread (the shell's objects are single-threaded).</summary>
    public static async Task<bool> OpenAsync(string folder, bool setView)
    {
        Directory.CreateDirectory(folder);
        using (Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true })) { }
        if (!setView) return false;
        try
        {
            var shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", throwOnError: true)!)!;
            for (var tries = 0; tries < 50; tries++)
            {
                await Task.Delay(200);
                // The window can be there before its view is: tried again until the view takes the columns.
                if (WindowOf(shell, folder) is { } window && TrySet(window)) return true;
            }
            Log.Write("folder", "File Explorer's window for the pictures folder wasn't found in time; its view is as Explorer chose");
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException or ArgumentException
                                       or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            Log.Error("folder", "Setting the pictures folder's view failed", ex);
        }
        return false;
    }

    /// <summary>The Explorer window (or tab) showing the folder, if one is.</summary>
    private static object? WindowOf(object shell, string folder)
    {
        foreach (var window in (System.Collections.IEnumerable)((dynamic)shell).Windows())
        {
            try
            {
                string path = ((dynamic)window).Document.Folder.Self.Path;
                if (string.Equals(Path.TrimEndingDirectorySeparator(path), Path.TrimEndingDirectorySeparator(folder), StringComparison.OrdinalIgnoreCase))
                    return window;
            }
            catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
            {
                // A window that isn't a folder (or is closing): not this one.
            }
        }
        return null;
    }

    /// <summary>Details view with <see cref="Columns"/>, and whether the view now has exactly them.</summary>
    private static bool TrySet(object window)
    {
        if (window is not IServiceProvider services) return false;
        var topLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837"); // SID_STopLevelBrowser
        var shellBrowser = typeof(IShellBrowser).GUID;
        if (services.QueryService(ref topLevelBrowser, ref shellBrowser, out var pointer) != 0 || pointer == IntPtr.Zero) return false;
        try
        {
            var browser = (IShellBrowser)Marshal.GetObjectForIUnknown(pointer);
            if (browser.QueryActiveShellView(out var view) != 0 || view is not IFolderView folderView || view is not IColumnManager columns) return false;
            folderView.SetCurrentViewMode(DetailsView);
            var keys = Columns.Select(c => c.Key).ToArray();
            columns.SetColumns(keys, (uint)keys.Length);
            columns.GetColumnCount(VisibleColumns, out var count);
            var shown = new PropertyKey[count];
            columns.GetColumns(VisibleColumns, shown, count);
            folderView.GetCurrentViewMode(out var mode);
            return mode == DetailsView && shown.SequenceEqual(keys);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly record struct PropertyKey(Guid FormatId, uint PropertyId);

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid service, ref Guid riid, out IntPtr obj);
    }

    // Only the methods used are typed; the ones before them hold their places in the vtable.
    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow(); void ContextSensitiveHelp(); void InsertMenusSB(); void SetMenuSB(); void RemoveMenusSB();
        void SetStatusTextSB(); void EnableModelessSB(); void TranslateAcceleratorSB(); void BrowseObject();
        void GetViewStateStream(); void GetControlWindow(); void SendControlMsg();
        [PreserveSig] int QueryActiveShellView([MarshalAs(UnmanagedType.IUnknown)] out object? view);
    }

    [ComImport, Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView
    {
        void GetCurrentViewMode(out uint mode);
        void SetCurrentViewMode(uint mode);
    }

    [ComImport, Guid("D8EC27BB-3F3B-4042-B10A-4ACFD924D453"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IColumnManager
    {
        void SetColumnInfo(); void GetColumnInfo();
        void GetColumnCount(uint flags, out uint count);
        void GetColumns(uint flags, [Out, MarshalAs(UnmanagedType.LPArray)] PropertyKey[] keys, uint count);
        void SetColumns([In, MarshalAs(UnmanagedType.LPArray)] PropertyKey[] keys, uint count);
    }
}
