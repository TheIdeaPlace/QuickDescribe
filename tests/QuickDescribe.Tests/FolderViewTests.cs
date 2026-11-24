using System.Windows.Threading;
using QuickDescribe.Services;

namespace QuickDescribe.Tests;

public class FolderViewTests
{
    [Fact]
    public void Columns_AreNameDateTitleCommentsSize()
    {
        Assert.Equal(["Name", "Date modified", "Title", "Comments", "Size"], FolderView.Columns.Select(c => c.Name));
        // System.Title and System.Comment: what Quick Describe writes, as Explorer reads them.
        Assert.Equal(new FolderView.PropertyKey(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2), FolderView.Columns[2].Key);
        Assert.Equal(new FolderView.PropertyKey(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 6), FolderView.Columns[3].Key);
    }

    [Fact]
    public void ViewIsSetOnce_NotForOlderSettings()
    {
        Assert.False(new Settings().FolderViewSet);
    }
}

/// <summary>Opens a File Explorer window on a folder of its own for a few seconds and sets its view, through the app's
/// own code. Only with QD_FOLDER_TRIAL=1: it opens a window on this PC.</summary>
public class FolderViewTrialTests
{
    [Fact]
    public void SetsDetailsViewWithTheColumns()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("QD_FOLDER_TRIAL") == "1", "Set QD_FOLDER_TRIAL=1 to run.");
        var folder = Directory.CreateTempSubdirectory("QuickDescribeFolderTrial").FullName;
        try
        {
            bool set = false;
            Exception? failure = null;
            // The shell's objects are single-threaded: an STA thread with a dispatcher, as the app's UI thread is.
            var thread = new Thread(() =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                Dispatcher.CurrentDispatcher.InvokeAsync(async () =>
                {
                    try { set = await FolderView.OpenAsync(folder, setView: true); }
                    catch (Exception ex) { failure = ex; }
                    finally
                    {
                        CloseWindows(folder);
                        Dispatcher.CurrentDispatcher.InvokeShutdown();
                    }
                });
                Dispatcher.Run();
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure is not null) throw failure;
            Assert.True(set);
        }
        finally
        {
            Thread.Sleep(500);
            Directory.Delete(folder, true);
        }
    }

    private static void CloseWindows(string folder)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        foreach (dynamic window in shell.Windows())
        {
            try
            {
                if (string.Equals((string)window.Document.Folder.Self.Path, folder, StringComparison.OrdinalIgnoreCase)) window.Quit();
            }
            catch (Exception) { /* not a folder window */ }
        }
    }
}
