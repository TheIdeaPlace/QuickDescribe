using System.Runtime.CompilerServices;
using QuickDescribe.Services;

namespace QuickDescribe.Tests;

/// <summary>Tests that read the data folder's files (the log, the crash flag) run one at a time.</summary>
[CollectionDefinition(Name)]
public sealed class SharedState
{
    public const string Name = "Shared state";
}

/// <summary>The whole test run uses a data folder of its own, set before any test runs, so nothing a test logs reaches the
/// real one in %LOCALAPPDATA%.</summary>
internal static class TestDataFolder
{
    public static readonly string Path = Directory.CreateTempSubdirectory("QuickDescribeData").FullName;

    [ModuleInitializer]
    internal static void Use() => AppPaths.DataFolder = Path;
}
