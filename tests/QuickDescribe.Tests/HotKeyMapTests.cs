using QuickDescribe.Services;
using QuickDescribe.Views;

namespace QuickDescribe.Tests;

/// <summary>The hotkeys as chosen in Settings: the keys held, each command's letter, and what's refused.</summary>
public class HotKeyMapTests
{
    private static HotKeyMap Map(KeyModifiers modifiers, char? screen = 'S', char? window = 'W', char? focus = 'F', char? clipboard = 'V') =>
        new(modifiers, new Dictionary<Target, char?>
        {
            [Target.Screen] = screen, [Target.Window] = window, [Target.Focus] = focus, [Target.Clipboard] = clipboard,
        });

    [Fact]
    public void TheDefaults_AreCtrlAltShift_WithSWFV()
    {
        var map = HotKeyMap.Default;
        Assert.Equal("Ctrl+Alt+Shift", map.ModifierText);
        Assert.Equal(["Ctrl+Alt+Shift+S", "Ctrl+Alt+Shift+W", "Ctrl+Alt+Shift+F", "Ctrl+Alt+Shift+V"],
            HotKeyMap.Targets.Select(map.GestureFor));
        Assert.Empty(map.Problems());
    }

    [Fact]
    public void Modifiers_AreWrittenInWindowsUsualOrder_WhateverOrderTheyWereChosen()
    {
        Assert.Equal("Ctrl+Alt+Shift+Windows", Map(KeyModifiers.Windows | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Ctrl).ModifierText);
        Assert.Equal("Alt+Windows+Q", Map(KeyModifiers.Windows | KeyModifiers.Alt, screen: 'Q').GestureFor(Target.Screen));
    }

    [Theory]
    [InlineData(KeyModifiers.None)]
    [InlineData(KeyModifiers.Shift)]
    public void ShiftAlone_OrNothing_IsRefused_SinceTheLettersAreForTyping(KeyModifiers modifiers)
    {
        var problem = Assert.Single(Map(modifiers).Problems());
        Assert.StartsWith("Hold Ctrl, Alt or the Windows key", problem);
    }

    [Theory]
    [InlineData(KeyModifiers.Ctrl | KeyModifiers.Shift)]
    [InlineData(KeyModifiers.Alt)]
    [InlineData(KeyModifiers.Windows)]
    public void AnyOfCtrlAltOrWindows_IsEnough(KeyModifiers modifiers) => Assert.Empty(Map(modifiers).Problems());

    [Fact]
    public void TheSameLetterTwice_IsRefused_NamingBoth()
    {
        var problem = Assert.Single(Map(KeyModifiers.Ctrl | KeyModifiers.Alt, focus: 'S').Problems());
        Assert.Equal("Screen and Focused item both use S. Give each its own letter.", problem);
    }

    [Fact]
    public void ACommandWithNoLetter_HasNoHotkey_AndThat_IsFine()
    {
        var map = Map(KeyModifiers.Ctrl | KeyModifiers.Alt, window: null, clipboard: null);
        Assert.Empty(map.Problems());
        Assert.Null(map.GestureFor(Target.Window));
        Assert.Equal("Screen, Ctrl+Alt+S. Focused item, Ctrl+Alt+F.", SettingsWindow.Summary(map));
        Assert.Equal("No hotkeys: the Describe and File menus still do everything.",
            SettingsWindow.Summary(Map(KeyModifiers.Ctrl, null, null, null, null)));
    }

    [Fact]
    public void TheMap_RoundTripsThroughSettings()
    {
        var settings = new Settings { HotKeys = Map(KeyModifiers.Alt | KeyModifiers.Windows, screen: 'Q', clipboard: null) };
        var path = Path.Combine(Path.GetTempPath(), $"qd-settings-{Guid.NewGuid():N}.json");
        try
        {
            settings.Save(path);
            var back = Settings.Load(path).HotKeys;
            Assert.True(back.SameAs(settings.HotKeys));
            Assert.Equal("Alt+Windows+Q", back.GestureFor(Target.Screen));
            Assert.Null(back.GestureFor(Target.Clipboard));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NoChoice_OrOneThatCantBeUsed_GivesTheDefaults()
    {
        Assert.True(new Settings().HotKeys.SameAs(HotKeyMap.Default));
        // Shift alone, written by hand or by an older version: the defaults, rather than taking letters from typing.
        Assert.True(HotKeyMap.FromSettings((int)KeyModifiers.Shift, new Dictionary<string, string> { ["Screen"] = "S" }).SameAs(HotKeyMap.Default));
        Assert.True(HotKeyMap.FromSettings((int)KeyModifiers.Ctrl, new Dictionary<string, string> { ["Screen"] = "S", ["Focus"] = "s" }).SameAs(HotKeyMap.Default));
        // A letter typed in lower case is the same letter; something that isn't a letter is refused.
        Assert.Equal("Ctrl+Q", HotKeyMap.FromSettings((int)KeyModifiers.Ctrl, new Dictionary<string, string> { ["Screen"] = "q" }).GestureFor(Target.Screen));
        Assert.True(HotKeyMap.FromSettings((int)KeyModifiers.Ctrl, new Dictionary<string, string> { ["Screen"] = "7" }).SameAs(HotKeyMap.Default));
    }

    [Fact]
    public void TheWelcome_SaysTheHotkeysAsTheyreSet()
    {
        var text = MainWindow.Welcome(Map(KeyModifiers.Ctrl | KeyModifiers.Windows, screen: 'Q', clipboard: null));
        Assert.StartsWith("From any app, press Ctrl+Windows with:" + Environment.NewLine + "Q to describe the screen,", text);
        Assert.Contains("File, Settings sets the others.", text);
        Assert.DoesNotContain("clipboard.", text.Split(Environment.NewLine + Environment.NewLine)[0]);
        Assert.StartsWith("No hotkeys are set", MainWindow.Welcome(Map(KeyModifiers.Ctrl, null, null, null, null)));
    }
}
