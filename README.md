# Quick Describe

You have something you want described, quickly. Press a key in any app and Quick Describe describes it: the screen, the active window, the item with focus, a picture you've copied, a picture file, or a picture it takes with your camera. It reads out the text in it too. Then the description is yours to use: paste it, or the picture and the description together, wherever you need it, and save it if you want to.

There are other tools that describe pictures. Quick Describe isn't tied to any one screen reader or online service. It uses the AI built into Windows, so on the right PC it just works: with JAWS, NVDA, Narrator or no screen reader at all, without an account, and without sending your pictures anywhere.

## What you need

- **Windows 11**, a recent version.
- **For descriptions, a PC with an AI chip**, called a neural processing unit or NPU. Windows' image description runs on it. Microsoft called these PCs **Copilot+ PCs**, and many are still sold with that name, though Microsoft no longer uses it for its new PCs. Not sure about yours? Install Quick Describe and choose **Help, Check Windows AI**.
- **On other PCs,** Quick Describe still reads the text in pictures; it just doesn't describe them.

## Installing

Download and open [QuickDescribe.appinstaller](https://github.com/TheIdeaPlace/QuickDescribe/releases/latest/download/QuickDescribe.appinstaller) from the [latest release](https://github.com/TheIdeaPlace/QuickDescribe/releases/latest), and choose Install. Quick Describe is then in the Start menu, and you can uninstall it from Settings, Apps like any app.

It updates itself: Windows checks every few hours and installs a new version while Quick Describe isn't running. **Help, Check for Updates** checks now.

## Describing something

Start Quick Describe. While it's running, these keys work from any app:

| Keys | Describes |
|---|---|
| Ctrl+Alt+Shift+S | The screen |
| Ctrl+Alt+Shift+W | The active window |
| Ctrl+Alt+Shift+F | The item with focus, and what's around it |
| Ctrl+Alt+Shift+V | The picture on the clipboard, or a picture file you've copied |
| Ctrl+Alt+Shift+M | A picture from your camera |

You can change these in **File, Settings**.

Quick Describe comes to the front and your screen reader says what it's describing. When it's done, you hear the description and how many lines of text it found. The whole result is in the Result box, where focus is, to read at your own pace: the description first, then "Text in the picture:" and the text.

Press **Escape** to go back to where you were. Quick Describe minimizes and the keys keep working. Escape also stops a description that's still going. If you switch away while it's describing, it waits: the result is spoken when you come back.

You can also open a picture (Ctrl+O) or drop one on the window. Quick Describe describes one picture at a time: copy or drop several and it describes the first.

**Describe, Description Type** chooses how much detail you get: Accessible (the default, written for people who are blind or have low vision), Detailed, Brief or Diagram.

### With the camera

Ctrl+Alt+Shift+M, or **Describe, Camera**, takes a picture and describes it. Hold what you want described in front of the camera until you hear "Describing Camera". It uses the camera on the back of a tablet if there is one, and otherwise the one facing you. The first time, Windows asks whether Quick Describe may use your camera; if it can't, Quick Describe tells you where to turn it on.

## Using a description

In Quick Describe:

| Keys | Does |
|---|---|
| Ctrl+Shift+C | Copies the result (the description and text) |
| Ctrl+Shift+S | Copies the picture |
| Ctrl+Shift+D | Copies the picture and its description together |
| Ctrl+S | Saves the picture |
| Ctrl+O | Opens a picture file and describes it |
| Ctrl+V | Describes the picture on the clipboard |
| Ctrl+Comma | Opens Settings |
| F1 | Lists the keys |

These are also in the Edit and File menus, and as buttons.

**Copy Image and Description** (Ctrl+Shift+D) puts both on the clipboard at once, and each app takes what it can use. Paste into Word and you get the picture, with the description as its alt text, and the description written out below it. Paste into a text box and you get the description. Paste into File Explorer and you get the picture file, with its description inside. Email and chat apps vary: some take both, some only the picture or only the text.

**Save Described Image** (Ctrl+S) saves the picture as a PNG or JPEG file, with a name that says what it was and when, such as "Active window, Notepad 2026-10-30 14.05.12.png".

## Keeping a record

If you'd like a record of what you've described, or don't want to describe the same picture twice, Quick Describe can save its pictures with their descriptions. It's all optional.

- **The description goes in the pictures Quick Describe saves.** It adds the description, and any text it read, to the picture file's details (its metadata): the **Title** and **Comments** you see in File Explorer, and the caption and alt text that photo apps read. In File Explorer, select a picture and open the Details pane (Alt+Shift+P), or turn on the Comments column, to read it. This is on unless you turn it off in Settings, and it applies whether you save with Ctrl+S or Quick Describe saves for you.
- **Save every picture** is off unless you turn it on in Settings. Then each picture Quick Describe describes, including ones you open or paste, is saved in a **Quick Describe** folder in your Pictures folder, with its description. **File, Open Quick Describe Pictures Folder** opens it, set up to show each picture's Title and Comments.
- **Your own pictures are never changed.** When you open, drop or paste one of your pictures, Quick Describe reads it and leaves the file as it was. Only its own copies get a description. Those copies don't keep a photo's own details, such as the date it was taken or where.
- **OneDrive.** If OneDrive backs up your Pictures folder, as it often does, it uploads these pictures too, and the text in them.

## Settings

**File, Settings** (Ctrl+Comma) has:

- **The hotkeys:** the keys you hold (Ctrl+Alt+Shift unless you change it) and a letter for each command, or none to turn one off. If another app already uses one, Settings tells you. Settings won't let you pick keys that would take over something every app uses, such as Ctrl alone, unless you check **Allow key combinations other apps use** and confirm.
- **The description type.**
- **Start Quick Describe when I sign in to Windows.** It starts quietly, out of the way.
- **Keep running in the notification area when I close the window,** so the hotkeys keep working. File, Exit then quits.
- **Save every picture**, and under it, whether to include pictures you open or drop.
- **Put the description and the text read in each picture Quick Describe saves.**

**Reset to Defaults** puts everything back.

## On a PC without an AI chip

Quick Describe works the same way, with the same keys, but gives you only the text in the picture. The first time, it tells you why. If your PC has the chip but Windows' AI features are turned off (Windows Settings, Privacy & security), it says so, since you can turn them on. **Help, Check Windows AI** says which applies.

## What to expect

- **Screens full of text get their text, not a description.** Windows' AI won't describe pictures that are mostly text, as most windows are, so Quick Describe gives you the text. A photo, a chart or the item with focus usually gets a description.
- **Descriptions can be wrong.** The AI sometimes gets colours, positions or small text wrong, or describes things that aren't there. The text Quick Describe reads is usually more reliable than text quoted in a description.
- **The first description takes longest,** up to half a minute while Windows loads its AI. After that, a few seconds.
- **In a web page,** with JAWS's or NVDA's virtual cursor, the item with focus is usually the whole page. Use Ctrl+Alt+Shift+W for the window instead.
- **You can't ask questions about a picture yet.** That needs Windows' on-device language model, which Microsoft lets apps use only with its permission.

## Privacy

Pictures are described on your PC by Windows itself, and never sent anywhere. Quick Describe takes a picture of the screen, or turns on the camera, only when you ask. Nothing is saved unless you save it or turn on Save every picture. Copy Image and Description keeps the last picture it copied in Quick Describe's own app folder, so File Explorer can paste it; the next copy replaces it. Quick Describe also keeps your settings and a log of what it did, for bug reports.

What goes over the internet:

- **Update checks,** which read the latest release from GitHub. Nothing about you or your pictures is sent.
- **Bug reports,** only when you send one. They're public on GitHub.
- **Windows' AI model,** which Windows may download the first time.

## Reporting a bug or suggesting something

Choose **Help, Report a Bug…**. It shows exactly what will be sent before you send it, and you don't need a GitHub account. **Everything in a report is public**, including any contact you give. If Quick Describe didn't quit properly last time, it offers to report that. **Help, Open Log Folder** shows its log, which stays on your PC unless you send it.

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

To use Windows AI from a development build on a PC with an NPU, turn on Developer Mode, then register the helper: `.\scripts\build-ai-helper.ps1 -Register`. `quickdescribe-ai --check` should then say `"state":"Ready"`. The tests that need Windows AI run only where it's ready, and skip themselves elsewhere, CI included. When you're done, `.\scripts\build-ai-helper.ps1 -Unregister` removes the registration. Always run it before deleting the folder: a registration whose folder is gone keeps the `quickdescribe-ai` command and breaks Windows AI.

`.\scripts\build-msix.ps1` builds the installer. Releases are built and signed by `.github/workflows/release.yml` when a `v<version>` tag is pushed: see CLAUDE.md.

## Licence

Quick Describe is free and open source, under the [MIT License](LICENSE).
