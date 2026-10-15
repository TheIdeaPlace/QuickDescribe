using QuickDescribe.Services;

namespace QuickDescribe.Tests;

public class PrivacyTests
{
    [Fact]
    public void TheUserFolder_AndTheUserName_AreHidden()
    {
        var text = Privacy.Redact(@"Couldn't read C:\Users\Kelly\Pictures\a.jpg for kelly on \server\kelly\b.jpg", @"C:\Users\Kelly", "kelly");
        Assert.Equal(@"Couldn't read %USERPROFILE%\Pictures\a.jpg for %USERNAME% on \server\%USERNAME%\b.jpg", text);
    }

    [Fact]
    public void TheUserName_IsOnlyReplacedAsAWholeWord()
    {
        Assert.Equal("also kellylford and %USERNAME%.", Privacy.Redact("also kellylford and Kelly.", @"C:\Users\Kelly", "kelly"));
        Assert.Equal("al also", Privacy.Redact("al also", @"C:\Users\al", "a"));
    }
}

/// <summary>The source as people will hear it: no text garbled by a wrong encoding. An edit once turned the "…" of
/// "Describing…" into three other characters (UTF-8 read as Windows-1252), which a screen reader reads as "a circumflex
/// euro".</summary>
public class SourceTextTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "QuickDescribe.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("The repository wasn't found above the tests.");
    }

    public static TheoryData<string> Files()
    {
        var root = RepoRoot();
        var data = new TheoryData<string>();
        foreach (var folder in new[] { "src", "tests", "docs", "scripts", "packaging" })
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, folder), "*.*", SearchOption.AllDirectories))
                if (Path.GetExtension(file) is ".cs" or ".xaml" or ".md" or ".ps1" or ".xml" && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                    && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                    data.Add(Path.GetRelativePath(root, file));
        data.Add("README.md");
        data.Add("CLAUDE.md");
        return data;
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void HasNoGarbledText(string file)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), file));
        // UTF-8 read as Windows-1252: "…" becomes "\u00e2\u20ac\u00a6", "é" becomes "\u00c3\u00a9".
        Assert.DoesNotContain("\u00e2\u20ac", text);
        Assert.DoesNotContain("\u00c3\u00a9", text);
        Assert.DoesNotContain("\u00c3\u00a8", text);
    }
}
