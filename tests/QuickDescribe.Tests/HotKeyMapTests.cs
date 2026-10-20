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
    public void ShiftAlone_OrNothing_IsRefused(KeyModifiers modifiers) =>
        Assert.StartsWith("Hold Ctrl, Alt or the Windows key with the letters", Assert.Single(Map(modifiers).Problems()));

    [Fact]
    public void KeysOtherAppsUse_AreNamedWithWhatTheyCost_BeforeTheyreKept()
    {
        Assert.Null(HotKeyMap.Default.ConflictWarning());
        var ctrlShift = Map(KeyModifiers.Ctrl | KeyModifiers.Shift, window: null, focus: null, clipboard: null) with { AllowConflicts = true };
        Assert.Equal("While Quick Describe runs, Ctrl+Shift+S will describe in every app, instead of what those keys do there " +
                     "(Ctrl+Shift+S is Save As, and Ctrl+Shift+V pastes as plain text, in many apps). Use them anyway?", ctrlShift.ConflictWarning());
        // Alt alone with F, D, E or H also takes Quick Describe's own menus.
        Assert.Contains("Quick Describe's own menus too", (Map(KeyModifiers.Alt) with { AllowConflicts = true }).ConflictWarning());
        Assert.DoesNotContain("own menus", (Map(KeyModifiers.Alt, 'Q', 'Z', 'X', 'Y') with { AllowConflicts = true }).ConflictWarning());
        // No hotkeys at all: nothing to warn about.
        Assert.Null(Map(KeyModifiers.Ctrl, null, null, null, null).ConflictWarning());
    }

    [Theory]
    [InlineData(KeyModifiers.Ctrl)]                      // Ctrl+V, Ctrl+S, Ctrl+F in every app
    [InlineData(KeyModifiers.Alt)]                       // every menu's access keys
    [InlineData(KeyModifiers.Ctrl | KeyModifiers.Shift)] // Save As, paste as plain text
    [InlineData(KeyModifiers.Alt | KeyModifiers.Shift)]
    public void KeysThatWouldTakeOtherAppsShortcuts_AreRefused(KeyModifiers modifiers)
    {
        var problem = Assert.Single(Map(modifiers).Problems());
        Assert.StartsWith("Hold at least two of Ctrl, Alt and the Windows key, or the Windows key", problem);
    }

    [Theory]
    [InlineData(KeyModifiers.Ctrl)]
    [InlineData(KeyModifiers.Alt)]
    [InlineData(KeyModifiers.Ctrl | KeyModifiers.Shift)]
    [InlineData(KeyModifiers.Alt | KeyModifiers.Shift)]
    public void AllowingConflicts_LetsCtrlOrAltAlone_AndCtrlShift_Through(KeyModifiers modifiers) =>
        Assert.Empty((Map(modifiers) with { AllowConflicts = true }).Problems());

    [Theory]
    [InlineData(KeyModifiers.None)]
    [InlineData(KeyModifiers.Shift)]
    public void ShiftAlone_IsRefused_EvenWhenConflictsAreAllowed(KeyModifiers modifiers)
    {
        var problem = Assert.Single((Map(modifiers) with { AllowConflicts = true }).Problems());
        Assert.StartsWith("Hold Ctrl, Alt or the Windows key with the letters", problem);
    }

    [Fact]
    public void AllowingConflicts_IsKept_AndLetsASavedCtrlShiftChoiceStand()
    {
        var settings = new Settings { HotKeys = Map(KeyModifiers.Ctrl | KeyModifiers.Shift, screen: 'Q') with { AllowConflicts = true } };
        Assert.True(settings.HotKeysAllowConflicts);
        Assert.Equal("Ctrl+Shift+Q", settings.HotKeys.GestureFor(Target.Screen));
        settings.HotKeysAllowConflicts = false;
        Assert.True(settings.HotKeys.SameAs(HotKeyMap.Default));
    }

    [Theory]
    [InlineData(KeyModifiers.Ctrl | KeyModifiers.Alt)]
    [InlineData(KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Shift)]
    [InlineData(KeyModifiers.Ctrl | KeyModifiers.Windows)]
    [InlineData(KeyModifiers.Alt | KeyModifiers.Windows)]
    [InlineData(KeyModifiers.Windows)]
    [InlineData(KeyModifiers.Windows | KeyModifiers.Shift)]
    public void TwoOfCtrlAltAndWindows_OrWindows_AreAllowed(KeyModifiers modifiers) => Assert.Empty(Map(modifiers).Problems());

    [Fact]
    public void OKWarnsAboutTakenHotkeys_OnlyWhenTheyChanged_AndOnlyOnce()
    {
        var opened = HotKeyMap.Default;
        var changed = Map(KeyModifiers.Ctrl | KeyModifiers.Alt, screen: 'Q');
        Assert.False(SettingsWindow.ShouldWarn(opened, changed, [], null));                       // nothing taken
        Assert.True(SettingsWindow.ShouldWarn(opened, changed, [Target.Screen], null));           // taken: said
        Assert.False(SettingsWindow.ShouldWarn(opened, changed, [Target.Screen], changed));       // OK again keeps them
        Assert.True(SettingsWindow.ShouldWarn(opened, Map(KeyModifiers.Ctrl | KeyModifiers.Alt, screen: 'Z'), [Target.Screen], changed));
        // The hotkeys weren't touched (only the description type was): said at the start, not again.
        Assert.False(SettingsWindow.ShouldWarn(opened, opened, [Target.Window], null));
    }

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
            SettingsWindow.Summary(Map(KeyModifiers.Ctrl | KeyModifiers.Alt, null, null, null, null)));
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
        const int ctrlAlt = (int)(KeyModifiers.Ctrl | KeyModifiers.Alt);
        Assert.True(HotKeyMap.FromSettings(ctrlAlt, new Dictionary<string, string> { ["Screen"] = "S", ["Focus"] = "s" }).SameAs(HotKeyMap.Default));
        // Ctrl alone, allowed by an earlier build: the defaults, rather than taking Ctrl+V from every app.
        Assert.True(HotKeyMap.FromSettings((int)KeyModifiers.Ctrl, new Dictionary<string, string> { ["Screen"] = "Q" }).SameAs(HotKeyMap.Default));
        // A letter typed in lower case is the same letter; something that isn't a letter is refused.
        Assert.Equal("Ctrl+Alt+Q", HotKeyMap.FromSettings(ctrlAlt, new Dictionary<string, string> { ["Screen"] = "q" }).GestureFor(Target.Screen));
        Assert.True(HotKeyMap.FromSettings(ctrlAlt, new Dictionary<string, string> { ["Screen"] = "7" }).SameAs(HotKeyMap.Default));
    }

    [Fact]
    public void TheWelcome_SaysTheHotkeysAsTheyreSet()
    {
        var text = MainWindow.Welcome(Map(KeyModifiers.Ctrl | KeyModifiers.Windows, screen: 'Q', clipboard: null));
        Assert.StartsWith("From any app, press Ctrl+Windows with:" + Environment.NewLine + "Q to describe the screen,", text);
        Assert.Contains("File, Settings sets the others.", text);
        Assert.DoesNotContain("clipboard.", text.Split(Environment.NewLine + Environment.NewLine)[0]);
        Assert.StartsWith("No hotkeys are set", MainWindow.Welcome(Map(KeyModifiers.Ctrl | KeyModifiers.Alt, null, null, null, null)));
    }

    [Fact]
    public void TheWelcome_OnAPCThatCantDescribe_SaysItReadsText()
    {
        var text = MainWindow.Welcome(HotKeyMap.Default, textOnly: true);
        Assert.Contains("This PC reads the text in pictures", text);
        Assert.DoesNotContain("Description Type", text);
        Assert.Contains("Description Type", MainWindow.Welcome(HotKeyMap.Default));
    }

    [Fact]
    public void RefusedHotkeys_AreListedPlainly()
    {
        var map = HotKeyMap.Default;
        Assert.Equal("Ctrl+Alt+Shift+S is already taken, by another app or by Windows itself, so it doesn't work from other apps.",
            map.Refused([Target.Screen]));
        Assert.Equal("Ctrl+Alt+Shift+S, Ctrl+Alt+Shift+W and Ctrl+Alt+Shift+V are already taken, by another app or by Windows itself, " +
                     "so they don't work from other apps.", map.Refused([Target.Screen, Target.Window, Target.Clipboard]));
    }
}
