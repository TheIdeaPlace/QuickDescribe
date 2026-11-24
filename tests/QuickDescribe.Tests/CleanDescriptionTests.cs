using QuickDescribe.Services;

namespace QuickDescribe.Tests;

public class CleanDescriptionTests
{
    [Fact]
    public void InvisibleCharacters_AreRemoved_AsWindowsGaveThem()
    {
        // What Windows' model put between "signed" and "installer" on 24 Nov 2026: a zero-width space, a combining grapheme
        // joiner, a word joiner and another zero-width space.
        Assert.Equal("a note about a signed installer.", Describing.Clean("a note about a signed ​͏⁠​installer."));
    }

    [Theory]
    [InlineData("soft­hyphen", "softhyphen")]
    [InlineData("﻿Starts with a byte order mark", "Starts with a byte order mark")]
    [InlineData("left‎ to right‏", "left to right")]
    [InlineData("two  ​  spaces", "two spaces")]
    [InlineData("  trimmed  ", "trimmed")]
    public void FormatCharacters_GoAndSpacesCloseUp(string from, string to) => Assert.Equal(to, Describing.Clean(from));

    [Fact]
    public void JoinersThatEmojiAndScriptsNeed_Stay()
    {
        var family = "👩‍👩‍👧";
        Assert.Equal($"A {family} emoji.", Describing.Clean($"A {family} emoji."));
        Assert.Equal("می‌خواهم", Describing.Clean("می‌خواهم"));
        Assert.Equal("Line one" + Environment.NewLine + "Line two", Describing.Clean("Line one" + Environment.NewLine + "Line two"));
    }
}
