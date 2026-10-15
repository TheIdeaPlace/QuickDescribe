# Quick Describe

Quick Describe tells you what's on your screen. Press a key from any app and it describes the screen, the active window or the item with focus, and reads out the text in it. It can describe a picture on the clipboard or a picture file too.

Everything happens on your PC, with the AI built into Windows on Copilot+ PCs. Nothing is uploaded, and there are no accounts. It's built for keyboard and screen reader use, and works with JAWS, NVDA and Narrator.

## What you need

- Windows 11 on a **Copilot+ PC**, for descriptions. Windows AI's image description runs on the PC's NPU, and only Copilot+ PCs have one.
- On PCs without Windows AI, Quick Describe still reads the text in pictures, with Windows' built-in text recognition. It just doesn't describe them.

## Installing

Download and open [QuickDescribe.appinstaller](https://github.com/TheIdeaPlace/QuickDescribe/releases/latest/download/QuickDescribe.appinstaller) from the [latest release](https://github.com/TheIdeaPlace/QuickDescribe/releases/latest). Windows' App Installer shows Quick Describe and its publisher, Kelly Ford, with an Install button. It installs the Windows App Runtime first if your PC needs it. Quick Describe is then in the Start menu, and uninstalls from Settings, Apps like any app. Windows checks for updates every few hours and installs them while Quick Describe isn't running.

## Using it

Start Quick Describe. While it's running, these keys work from any app:

| Keys | Describes |
|---|---|
| Ctrl+Alt+Shift+S | The screen the active window is on |
| Ctrl+Alt+Shift+W | The active window |
| Ctrl+Alt+Shift+F | The item with keyboard focus, and what's around it |
| Ctrl+Alt+Shift+V | A picture on the clipboard, or a picture file you've copied |

Quick Describe comes to the front with "Describing…", and your screen reader says what it's describing. When it's done, it speaks the description and how many lines of text it found. The full result is in the Result box, where focus is, to read by line, word or character: the description first, then "Text in the picture:" and the text, line by line.

Press **Escape** to go back to where you were. Quick Describe minimizes, and the hotkeys keep working. Pressed while it's still describing, Escape also stops that. If you switch to another app while it's describing, it doesn't pull you back or talk over that app: the result is waiting in Quick Describe when you return. **Alt+F4** quits, and the hotkeys stop.

Quick Describe never appears in its own pictures, so a screen described while it's showing is the screen behind it. A hotkey pressed while Quick Describe's Open Picture dialog is up just beeps.

In Quick Describe itself:

| Keys | Does |
|---|---|
| Ctrl+O | Opens a picture file and describes it |
| Ctrl+V | Describes the picture on the clipboard |
| Ctrl+Shift+C | Copies the result |
| F1 | Shows the keys |
| Escape | Stops describing, and goes back to where you were |

You can also drop a picture file on the window. The Describe menu does the same as the hotkeys: it steps out of the way first, so it describes what's behind Quick Describe. **Describe, Description Type** chooses how much detail you get: Accessible (the default, written for people who are blind or have low vision), Detailed, Brief or Diagram. **Help, Check Windows AI** says whether your PC can describe pictures, and why not if it can't.

If another app already uses one of the hotkeys, Quick Describe says which when it starts, and the Describe menu still works.

## What to expect

- **Screens full of text usually get their text, not a description.** Windows' image description declines pictures that are mostly text, which most windows and screens are. Quick Describe then says "No description: Windows' AI doesn't describe pictures with this much text," and gives you the text. The focused item, a photo or a chart usually gets a description.
- **Descriptions can be wrong.** The model sometimes gets colours, positions or small text wrong, and can describe things that aren't there. The text Quick Describe reads is usually more reliable than text quoted in a description, so check one against the other.
- **The first description takes longest** (up to half a minute) while Windows loads its model. After that, a few seconds.
- **Focus in a web page.** With JAWS's or NVDA's virtual cursor in a browser, the focused item is usually the whole page, so Ctrl+Alt+Shift+F describes the page. Use Ctrl+Alt+Shift+W for the window.
- **You can't ask questions about a picture yet.** That needs Phi Silica, Windows' on-device language model, which Microsoft lets apps use only with its permission.

## Privacy

Pictures are described and read on your PC by Windows itself. Quick Describe doesn't send anything over the internet; the first time, Windows may download its AI model. The screen is captured only when you press a hotkey or choose a command, pictures and results aren't saved, and the only thing Quick Describe keeps is your choice of description type.

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
