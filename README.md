# Quick Describe

Quick Describe tells you what's on your screen. Press a key from any app and it describes the screen, the active window or the item with focus, and reads out the text in it. It can describe a picture on the clipboard or a picture file too.

Everything happens on your PC, with the AI built into Windows on Copilot+ PCs. Nothing is uploaded, and there are no accounts. It's built for keyboard and screen reader use, and works with JAWS, NVDA and Narrator.

## What you need

- Windows 11 on a **Copilot+ PC**, for descriptions. Windows AI's image description runs on the PC's NPU, and only Copilot+ PCs have one.
- On PCs without Windows AI, Quick Describe still reads the text in pictures, with Windows' built-in text recognition. It just doesn't describe them.

## Installing

Download and open [QuickDescribe.appinstaller](https://github.com/TheIdeaPlace/QuickDescribe/releases/latest/download/QuickDescribe.appinstaller) from the [latest release](https://github.com/TheIdeaPlace/QuickDescribe/releases/latest). Windows' App Installer shows Quick Describe and its publisher, Kelly Ford, with an Install button. It installs the Windows App Runtime first if your PC needs it. Quick Describe is then in the Start menu, and uninstalls from Settings, Apps like any app. Windows checks for updates every few hours and installs them while Quick Describe isn't running. **Help, Check for Updates** checks now; it says which version you're running, and once an update is found, which version is available. The first time Quick Describe starts after an update, it says so, with See what's new.

## Using it

Start Quick Describe. While it's running, these keys work from any app:

| Keys | Describes |
|---|---|
| Ctrl+Alt+Shift+S | The screen the active window is on |
| Ctrl+Alt+Shift+W | The active window |
| Ctrl+Alt+Shift+F | The item with keyboard focus, and what's around it |
| Ctrl+Alt+Shift+V | A picture on the clipboard, or a picture file you've copied |

These are the defaults; **File, Settings** changes them (see below).

Quick Describe comes to the front with "Describing…", and your screen reader says what it's describing. When it's done, it speaks the description and how many lines of text it found. The full result is in the Result box, where focus is, to read by line, word or character: the description first, then "Text in the picture:" and the text, line by line.

Press **Escape** to go back to where you were. Quick Describe minimizes, and the hotkeys keep working. Pressed while it's still describing, Escape also stops that. If you switch to another app while it's describing, it doesn't pull you back or talk over that app: the result is waiting in Quick Describe, and spoken, when you return. **Alt+F4** quits, and the hotkeys stop.

Quick Describe leaves itself out of its own pictures, so a screen described while it's showing is the screen behind it. (Only for that moment: screen sharing and screenshots see it as usual.) A hotkey pressed while Quick Describe's Open Picture dialog is up just beeps.

In Quick Describe itself:

| Keys | Does |
|---|---|
| Ctrl+O | Opens a picture file and describes it |
| Ctrl+Comma | Opens Settings |
| Ctrl+V | Describes the picture on the clipboard |
| Ctrl+Shift+C | Copies the result |
| F1 | Shows the keys |
| Escape | Stops describing, and goes back to where you were |

You can also drop a picture file on the window. The Describe menu does the same as the hotkeys: it steps out of the way first, so it describes what's behind Quick Describe. **Describe, Description Type** chooses how much detail you get: Accessible (the default, written for people who are blind or have low vision), Detailed, Brief or Diagram. **Help, Check Windows AI** says whether your PC can describe pictures, and why not if it can't.

## Reporting a bug or suggesting something

Choose **Help, Report a Bug…**. The form asks what happened, what you expected and the steps (or, for a suggestion, what you'd like), with an optional contact, and shows exactly what will be sent. Send files it as an issue on GitHub for you; you don't need a GitHub account. **Everything in a report is public**, the contact included. It includes your Quick Describe and Windows versions and what Windows AI says it can do, unless you untick Include the system check. Your user name is written as %USERPROFILE%. If a report can't be sent, Quick Describe copies it and offers to open it on GitHub, filled in, for you to submit there.

If Quick Describe didn't quit properly last time, it offers once to report that, with the end of its log. **Help, Open Log Folder** shows that log: what Quick Describe started with and what went wrong, one file a day, kept for two weeks. It stays on your PC unless you send it.

If another app already uses one of the hotkeys, Quick Describe says which when it starts, and the Describe menu still works.

## Settings

**File, Settings** (Ctrl+Comma) chooses:

- **The keys held with every hotkey:** Ctrl, Alt, Shift and the Windows key, as check boxes. Hold at least two of Ctrl, Alt and the Windows key, or the Windows key: with Ctrl or Alt alone, a hotkey would take that key from every app (Ctrl+V would stop pasting anywhere). To use Ctrl+Shift, Alt+Shift, or Ctrl or Alt alone, anyway, check **Allow key combinations other apps use**, which is off unless you choose it. OK then asks you to confirm, naming what you give up: while Quick Describe runs, those keys describe instead of doing what they do in other apps (Ctrl+Shift+S would describe instead of Save As). With Alt alone, Alt+F, Alt+D, Alt+E and Alt+H also stop opening Quick Describe's own menus; Ctrl+Comma still opens Settings. Shift alone is never allowed, since the letters would stop typing. On many keyboards outside the US, Ctrl+Alt is AltGr, which types accented letters; if a hotkey gets in the way of one, choose another letter, or hold the Windows key instead.
- **A letter for each command:** Screen, Active window, Focused item and Clipboard picture, from A to Z, or None to turn that hotkey off. Each needs its own letter.
- **The description type:** Accessible, Detailed, Brief or Diagram, as in the Describe menu.

OK tries the hotkeys with Windows. If another app (or Windows itself, which keeps many Windows key combinations) already has one, Settings says which, so you can choose another letter; OK again keeps it anyway. **Reset to Defaults** goes back to Ctrl+Alt+Shift with S, W, F and V, and Accessible descriptions.

## What to expect

- **Screens full of text usually get their text, not a description.** Windows' image description declines pictures that are mostly text, which most windows and screens are. Quick Describe then says "No description: Windows' AI doesn't describe pictures with this much text," and gives you the text. The focused item, a photo or a chart usually gets a description.
- **Descriptions can be wrong.** The model sometimes gets colours, positions or small text wrong, and can describe things that aren't there. The text Quick Describe reads is usually more reliable than text quoted in a description, so check one against the other.
- **The first description takes longest** (up to half a minute) while Windows loads its model. After that, a few seconds.
- **Focus in a web page.** With JAWS's or NVDA's virtual cursor in a browser, the focused item is usually the whole page, so Ctrl+Alt+Shift+F describes the page. Use Ctrl+Alt+Shift+W for the window.
- **You can't ask questions about a picture yet.** That needs Phi Silica, Windows' on-device language model, which Microsoft lets apps use only with its permission.

## Privacy

Pictures are described and read on your PC by Windows itself, and never sent anywhere. The screen is captured only when you press a hotkey or choose a command, and pictures and results aren't saved. Quick Describe keeps your choice of description type, what its update checks found, and its log.

What goes over the internet:

- **Update checks.** When Quick Describe starts and while it runs, at most once a day, and when you choose Check for Updates, Quick Describe and Windows read the release information from GitHub, where Quick Describe is published. Nothing about you or your pictures is sent.
- **Bug reports,** only when you press Send in Report a Bug. They go through The Idea Place's bug-report relay to GitHub, where they're public.
- **Windows' AI model.** The first time, Windows may download it.

## For developers

- `src/QuickDescribe`: the app (WPF, .NET 10).
- `src/QuickDescribeAI`: the Windows AI helper. Windows lets only a packaged app use its AI models, so the app runs this small packaged program and talks to it in JSON lines. It describes pictures with `ImageDescriptionGenerator` and reads text with `TextRecognizer`. It's adapted from the helpers in [My Photo Library Described](https://github.com/TheIdeaPlace/MyPhotoLibraryDescribed) and [Image Description Toolkit](https://github.com/TheIdeaPlace/Image-Description-Toolkit).
- `tests/`: the app's tests, and the helper's protocol tests.
- `packaging/` and `scripts/build-msix.ps1`: the installer.

Build and test from PowerShell or cmd, not Git Bash. Started from Git Bash, the AI helper answers every picture with an internal error.

```powershell
dotnet build QuickDescribe.sln
dotnet test --solution QuickDescribe.sln
```

To use Windows AI from a development build on a Copilot+ PC, turn on Developer Mode, then register the helper: `.\scripts\build-ai-helper.ps1 -Register`. `quickdescribe-ai --check` should then say `"state":"Ready"`. The tests that need Windows AI run only where it's ready, and skip themselves elsewhere, CI included.

`.\scripts\build-msix.ps1` builds the installer. `-Sign` signs it with Kelly Ford's Azure Artifact Signing certificate. Releases are built and signed by `.github/workflows/release.yml` when a `v<version>` tag is pushed: see CLAUDE.md.

## Licence

Quick Describe is free and open source, under the [MIT License](LICENSE).
