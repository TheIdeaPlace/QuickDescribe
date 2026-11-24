# Quick Describe

Quick Describe tells you what's on your screen. Press a key from any app and it describes the screen, the active window or the item with focus, and reads out the text in it. It can describe a picture on the clipboard, a picture file, or a picture it takes with your PC's camera too.

Everything happens on your PC, with the AI built into Windows on Copilot+ PCs. Nothing is uploaded, and there are no accounts. It's built for keyboard and screen reader use, and works with JAWS, NVDA and Narrator.

## What you need

- Windows 11 on a **Copilot+ PC**, for descriptions. Windows AI's image description runs on the PC's NPU, and only Copilot+ PCs have one.
- On other PCs, Quick Describe reads the text in pictures, with Windows' built-in text recognition, but doesn't describe them. See "On a PC that isn't a Copilot+ PC" below.

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
| Ctrl+Alt+Shift+M | A picture it takes with the camera |

These are the defaults; **File, Settings** changes them (see below).

Quick Describe comes to the front with "Describing…", and your screen reader says what it's describing. When it's done, it speaks the description and how many lines of text it found. The full result is in the Result box, where focus is, to read by line, word or character: the description first, then "Text in the picture:" and the text, line by line.

Press **Escape** to go back to where you were. Quick Describe minimizes, and the hotkeys keep working. Pressed while it's still describing, Escape also stops that. If you switch to another app while it's describing, it doesn't pull you back or talk over that app: the result is waiting in Quick Describe, and spoken, when you return. **Alt+F4** quits, and the hotkeys stop, unless you've chosen to keep Quick Describe running (see Settings): then Alt+F4 closes the window and **File, Exit** quits.

Quick Describe leaves itself out of its own pictures, so a screen described while it's showing is the screen behind it. (Only for that moment: screen sharing and screenshots see it as usual.) A hotkey pressed while Quick Describe's Open Picture dialog is up just beeps.

In Quick Describe itself:

| Keys | Does |
|---|---|
| Ctrl+O | Opens a picture file and describes it |
| Ctrl+Comma | Opens Settings |
| Ctrl+V | Describes the picture on the clipboard |
| Ctrl+S | Saves the picture just described |
| Ctrl+Shift+S | Copies the picture just described to the clipboard |
| Ctrl+Shift+C | Copies the result |
| F1 | Shows the keys |
| Escape | Stops describing, and goes back to where you were |

**File, Save Described Image** (Ctrl+S, or the Save Described Image button) saves the picture Quick Describe just described: the screen, the active window, the focused item, the camera's picture, a clipboard picture or a picture file, as a PNG or JPEG file. It suggests a name that says what it was and when, such as "Active window, Notepad 2026-10-30 14.05.12.png", in your Pictures folder the first time and then wherever you saved last. Your screen reader says "Saved" and the file's name. The picture is the one for the result showing: describing something else replaces it. The description and the text read go in the file too, in whatever folder you save it, as they do in the copies Save every picture keeps: one setting covers both, **Put the description and the text read in each picture Quick Describe saves** in Settings, on unless you turn it off. A picture saved this way doesn't keep a photo's own details, such as when and where it was taken; your own file isn't changed.

**File, Open Quick Describe Pictures Folder** opens Pictures\Quick Describe, where Save every picture keeps its copies, in File Explorer. The first time, Quick Describe sets that folder to Details view with the columns that show what it put in its pictures: Name, Date modified, Title (the description's first one or two sentences), Comments (the whole description, then the text in the picture) and Size. File Explorer remembers the view from then on, however you open the folder, and if you change it, Quick Describe leaves your choice alone. Opened another way before you first use Open Quick Describe Pictures Folder, the folder starts with File Explorer's usual view.

**Edit, Copy Described Image** (Ctrl+Shift+S, or the Copy Described Image button) puts the same picture on the clipboard instead, to paste into an email, a document or a chat. Your screen reader says "Image copied".

### The camera

**Describe, Camera** (Ctrl+Alt+Shift+M, or the Describe Camera button) takes a picture with your PC's camera and describes it, with the text in it, as it does the screen. Quick Describe comes to the front and says "Taking a picture with the camera"; the camera's light comes on for a second or two while it adjusts to the light, and goes off once the picture is taken. Hold what you want described in front of the camera, or point it, until you hear "Describing Camera".

- **Which camera:** the one on the back of a tablet, if there is one, since it faces away from you; otherwise the one facing you; otherwise a camera you've plugged in. Most laptops have only the one facing you, so hold a document or an object up to it. With more than one camera, the result says which was used.
- **The first time,** Windows asks whether Quick Describe may use your camera. If you say no, or camera access is off in Windows Settings, Privacy & security, Camera, Quick Describe says so, and that's where to turn it on.
- **If another app is using the camera** (a video call, say), Quick Describe usually shares it. If it can't get a picture, it says so: close the other app and try again.
- **Windows Studio Effects** on Copilot+ PCs (background blur, for one) change the picture for every app, Quick Describe included: a blurred background is described as blurred. They're in Windows Settings, Bluetooth & devices, Cameras.
- **Save Described Image** and **Copy Described Image** work on the camera's picture too.

You can also drop a picture file on the window. The Describe menu does the same as the hotkeys: it steps out of the way first, so it describes what's behind Quick Describe. **Describe, Description Type** chooses how much detail you get: Accessible (the default, written for people who are blind or have low vision), Detailed, Brief or Diagram. **Help, Check Windows AI** says whether your PC can describe pictures, and why not if it can't.

## Reporting a bug or suggesting something

Choose **Help, Report a Bug…**. The form asks what happened, what you expected and the steps (or, for a suggestion, what you'd like), with an optional contact, and shows exactly what will be sent. Send files it as an issue on GitHub for you; you don't need a GitHub account. **Everything in a report is public**, the contact included. It includes your Quick Describe and Windows versions and what Windows AI says it can do, unless you untick Include the system check. Your user name is written as %USERPROFILE%. If a report can't be sent, Quick Describe copies it and offers to open it on GitHub, filled in, for you to submit there.

If Quick Describe didn't quit properly last time, it offers once to report that, with the end of its log. **Help, Open Log Folder** shows that log: what Quick Describe started with and what went wrong, one file a day, kept for two weeks. It stays on your PC unless you send it.

If another app already uses one of the hotkeys, Quick Describe says which when it starts, and the Describe menu still works.

## Settings

**File, Settings** (Ctrl+Comma) chooses:

- **The keys held with every hotkey:** Ctrl, Alt, Shift and the Windows key, as check boxes. Hold at least two of Ctrl, Alt and the Windows key, or the Windows key: with Ctrl or Alt alone, a hotkey would take that key from every app (Ctrl+V would stop pasting anywhere). To use Ctrl+Shift, Alt+Shift, or Ctrl or Alt alone, anyway, check **Allow key combinations other apps use**, which is off unless you choose it. OK then asks you to confirm, naming what you give up: while Quick Describe runs, those keys describe instead of doing what they do in other apps (Ctrl+Shift+S would describe instead of Save As). With Alt alone, Alt+F, Alt+D, Alt+E and Alt+H also stop opening Quick Describe's own menus; Ctrl+Comma still opens Settings. Shift alone is never allowed, since the letters would stop typing. On many keyboards outside the US, Ctrl+Alt is AltGr, which types accented letters; if a hotkey gets in the way of one, choose another letter, or hold the Windows key instead.
- **A letter for each command:** Screen, Active window, Focused item, Clipboard picture and Camera, from A to Z, or None to turn that hotkey off. Each needs its own letter.
- **The description type:** Accessible, Detailed, Brief or Diagram, as in the Describe menu.
- **Start Quick Describe when I sign in to Windows:** off unless you turn it on. Windows then starts it out of the way, without taking focus: in the notification area if it keeps running (below), and otherwise minimized on the taskbar. Anything it would ask at the start, such as saying an update was installed, waits until you first come to its window, or, if you've only used hotkeys, until you first press Escape. Quick Describe is then listed in Task Manager's Startup apps and in Windows Settings, Apps, Startup, where you can turn it off too. Turned off there, only you can turn it on again, there: Settings says so next to the check box, and if you check it anyway, offers to open the page. Only the installed copy can start at sign-in.
- **Keep running in the notification area when I close the window:** off unless you turn it on. Quick Describe's icon is then in the notification area the whole time, and closing the window (Alt+F4, or its Close button) hides it and leaves the hotkeys working; the first time, Quick Describe says where it went. A hotkey brings it back with the result, and so does choosing its icon (Windows+B, then the arrow keys and Enter; new icons start in the hidden icons, behind Show Hidden Icons). The icon's menu (Shift+F10 or the Applications key) has Open and Exit. **File, Exit** quits. Escape still minimizes to the taskbar, as it always does.
- **Save a copy of every picture Quick Describe describes:** off unless you turn it on. Each picture it describes is saved in a **Quick Describe** folder in your Pictures folder, named with what it was and when, as Save Described Image would suggest: "Screen 2026-11-19 10.41.09.png". The screen, windows, focused items and clipboard pictures are saved as PNG, which keeps text sharp, and the camera's pictures as JPEG. Picture files you open, drop or copy in File Explorer are copied too, with their description in the copy, so you don't have to describe them again; your own file isn't changed. Photos (JPEG, HEIC, AVIF, WebP) are copied as JPEG and other pictures as PNG. To keep only what Quick Describe captures, turn off **When saving every picture, also copy picture files I open or drop**, just below. Pictures already in Pictures\Quick Describe aren't copied again. If your Pictures folder is backed up to OneDrive, as it often is, these copies are uploaded with it, screens and all. If a picture can't be saved, the result says so, once each time Quick Describe runs.
- **Put the description and the text read in each picture Quick Describe saves:** on unless you turn it off. Pictures saved with Save Described Image, in whatever folder, and the copies of every picture, carry their description inside the file, so File Explorer and photo apps can show it and Windows Search can find it. Quick Describe uses the standard fields: the caption holds the description and then the text in the picture, the accessibility alt text holds the description (shortened to 250 characters when it's longer, with the whole of it in the extended description), File Explorer's Title holds the description's first one or two sentences, and its **Comments** hold the whole caption: in File Explorer, turn on the Comments column (or look in the Details pane) to read a saved picture's full description and text. Invisible characters Windows' AI sometimes puts in a description (zero-width spaces and the like) are taken out, in the Result box as well as the file. A screen that wasn't described gets its text but no title. Only the text that was read goes in, not lines such as "No description". A copy of every picture is saved as soon as it's taken, and the description is added once it's ready, so a picture is kept even if describing fails. Since the text read goes in the file, a screenshot's text goes wherever the file goes, OneDrive included.

OK tries the hotkeys with Windows. If another app (or Windows itself, which keeps many Windows key combinations) already has one, Settings says which, so you can choose another letter; OK again keeps it anyway. **Reset to Defaults** goes back to Ctrl+Alt+Shift with S, W, F, V and M, Accessible descriptions, putting the description in each picture saved on, and the other check boxes off.

## On a PC that isn't a Copilot+ PC

Quick Describe works the same way, with the same hotkeys, but gives you only the text in the picture. You hear how many lines of text it found, and the Result box has "Text in the picture:" and the lines. The first time, it says once why: "This PC can't describe pictures: Windows' image description needs a Copilot+ PC and a recent version of Windows 11. Quick Describe reads the text in them instead." After that, results are just the text. The welcome text and F1 say this PC reads text only. The description type has no effect on these PCs.

If your PC is a Copilot+ PC but Windows' AI features are turned off (Settings, Privacy & security), each result says there's no description, since you can turn them back on. **Help, Check Windows AI** says which applies.

## What to expect

- **Screens full of text usually get their text, not a description.** Windows' image description declines pictures that are mostly text, which most windows and screens are. Quick Describe then says "No description: Windows' AI doesn't describe pictures with this much text," and gives you the text. The focused item, a photo or a chart usually gets a description.
- **Descriptions can be wrong.** The model sometimes gets colours, positions or small text wrong, and can describe things that aren't there. The text Quick Describe reads is usually more reliable than text quoted in a description, so check one against the other.
- **The first description takes longest** (up to half a minute) while Windows loads its model. After that, a few seconds.
- **Focus in a web page.** With JAWS's or NVDA's virtual cursor in a browser, the focused item is usually the whole page, so Ctrl+Alt+Shift+F describes the page. Use Ctrl+Alt+Shift+W for the window.
- **You can't ask questions about a picture yet.** That needs Phi Silica, Windows' on-device language model, which Microsoft lets apps use only with its permission.

## Privacy

Pictures are described and read on your PC by Windows itself, and never sent anywhere. The screen is captured, and the camera turned on, only when you press a hotkey or choose a command, and results aren't saved. Pictures aren't either, unless you save or copy one, or turn on saving every picture in Settings (and if your Pictures folder is backed up to OneDrive, those copies are uploaded with it). Quick Describe keeps your settings, what its update checks found, and its log.

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

To use Windows AI from a development build on a Copilot+ PC, turn on Developer Mode, then register the helper: `.\scripts\build-ai-helper.ps1 -Register`. `quickdescribe-ai --check` should then say `"state":"Ready"`. The tests that need Windows AI run only where it's ready, and skip themselves elsewhere, CI included. When you're done, `.\scripts\build-ai-helper.ps1 -Unregister` removes the registration. Always run it before deleting the folder: a registration whose folder is gone keeps the `quickdescribe-ai` command and breaks Windows AI.

`.\scripts\build-msix.ps1` builds the installer. `-Sign` signs it with Kelly Ford's Azure Artifact Signing certificate. Releases are built and signed by `.github/workflows/release.yml` when a `v<version>` tag is pushed: see CLAUDE.md.

## Licence

Quick Describe is free and open source, under the [MIT License](LICENSE).
