using QuickDescribe.Services;

namespace QuickDescribe.Tests;

/// <summary>Tests that use the data folder (the log, the crash flag) run one at a time, in a folder of their own, never
/// in the real one.</summary>
[CollectionDefinition(Name)]
public sealed class SharedState : ICollectionFixture<SharedState.DataFolder>
{
    public const string Name = "Shared state";

    public sealed class DataFolder : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("QuickDescribeData").FullName;

        public DataFolder() => AppPaths.DataFolder = Path;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
        }
    }
}
