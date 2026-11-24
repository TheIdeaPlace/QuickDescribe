# CLAUDE.md

Quick Describe: a small Windows app that describes the screen, the active window, the focused item, a clipboard picture, a picture file or a camera picture, with Windows' on-device AI, and reads the text in it. Kept deliberately simple (Kelly, 15 Oct 2026): on-device only, no cloud providers, few settings: the hotkeys, the description type, starting at sign-in, keeping running when closed, saving every picture described and putting the description in pictures saved.

## Layout

- `src/QuickDescribe` — the WPF app. `Services/` holds everything testable: capture geometry (`PixelRect`), capture (`ScreenCapture`), the camera (`Camera`), pictures and clipboard (`Pictures`), text (`TextRecognition`), the helper client (`WindowsAI`), putting a result together (`Result`, `Describing`), hotkeys, settings, announcements. `Views/MainWindow` is the only window.
- `src/QuickDescribeAI` — the packaged Windows AI helper, adapted from My Photo Library Described's `PhotoFinderAI` (same JSON-lines protocol, plus `"op": "read"` for `TextRecognizer`). Windows only lets a packaged app use its AI models; the app starts this from its package's `AI\` folder, or in development through the `quickdescribe-ai.exe` alias a registered helper adds.
- `tests/QuickDescribe.Tests`, `tests/QuickDescribeAI.Tests` — no test opens a window. Tests that need Windows AI skip themselves unless the helper is installed and the model is ready.
- `packaging/`, `scripts/build-msix.ps1`, `scripts/sign-files.ps1` — the MSIX installer and signing, copied from My Photo Library Described.

## Building and testing

From PowerShell or cmd, never Git Bash (the helper answers every picture with internal_error when started from Git Bash):

```powershell
dotnet build QuickDescribe.sln
dotnet test --solution QuickDescribe.sln
.\scripts\build-ai-helper.ps1 -Register   # Developer Mode; Windows AI in development builds
.\scripts\build-ai-helper.ps1 -Unregister # undo it when done (builds nothing)
```

After changing the helper (`src/QuickDescribeAI`), run `.\scripts\build-ai-helper.ps1 -Register` again: development builds and the live tests use the registered helper, not the one just built. An installed Quick Describe claims the same `quickdescribe-ai` command, so remove it (`Get-AppxPackage TheIdeaPlace.QuickDescribe | Remove-AppxPackage`) while working on the helper.

**When you're done with the helper, run `.\scripts\build-ai-helper.ps1 -Unregister`, and reinstall Quick Describe if you removed it. Always do this before deleting the folder or worktree the registered build lives in.** A `-Register` registration runs from its build folder. The `quickdescribe-ai` command belongs to whichever package registered it first, so with Quick Describe removed, your build owns it, and keeps it even after Quick Describe is reinstalled. If the folder then goes, the registration stays behind with the command and Windows AI fails with "The system cannot find the path specified". A stale registration of IDT's helper (registered from a worktree a day before IDT's installer ran, worktree deleted later) broke Windows AI on Kelly's Surface in November 2026. `-Register` says which version answers the command, and `-Unregister` checks that it answers afterwards.

Drawn text comes out blank when Windows' session has no display (Kelly's remote session disconnected), so the tests that draw text fail on this PC then; CI runs them.

`QD_SCREEN_TRIAL=1` runs `ScreenTrialTests`, which captures this PC's real screen, window and focused item and prints only outcomes and timings. `QD_CAMERA_TRIAL=1` runs `CameraTrialTests`, which takes one real camera picture and describes it. While the PC is locked the camera answers Access is denied, though camera access is allowed (19 Nov 2026); unlocked, it took a 1920x1080 picture in 1.1 s.

Windows AI needs the Copilot+ NPU, which the vmtest VMs don't have, so this app is tested on Kelly's PC (Kelly, 15 Oct 2026). Keep such runs short and close what you open.

## Things learned the hard way

- Windows AI's `ImageDescriptionGenerator` refuses most screens and windows with `ImageHasTooMuchText` (WindowsAppSDK #6855). Shrinking the picture until it accepts (about 256 pixels) gets descriptions that invent text ("CiWsers Veel"), so Quick Describe doesn't; it shows the text instead.
- Windows AI's `TextRecognizer` reads screens far better than `Windows.Media.Ocr` (it keeps the "4" of "42 Harbor Road" beside a text box border, which the older OCR drops at every scale). The app uses it through the helper and falls back to `Windows.Media.Ocr` on PCs without it.
- The focused item's area is sized in 100%-scaling pixels and multiplied by the monitor's scale; a fixed pixel size cut labels in half at 200%.
- `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` keeps the window out of a GDI `BitBlt` of the screen too (checked on Kelly's PC), so a screen taken while Quick Describe shows doesn't read its own last result back. It's set only around the capture: left on, it hides the window from screen sharing, screenshots and perhaps magnifiers.
- `Requests` owns describing's sequencing: a new request or Escape cancels the last; messages (F1, Help) never do, or a result on its way is lost.
- Windows AI can stop answering altogether while `--check` still says Ready: on 17 Oct 2026, after its `WindowsWorkload.*` components updated without a restart, describing and text recognition both hung (loading the models still worked) until the PC was restarted. So a request that times out (90 seconds, or 60 for each warm-up request, counted from when it reaches the helper) says restarting may help, and one waiting behind it gives up at once, so the text comes from the built-in OCR. Because of that limit, model downloads never happen inside a request: the app gets models ready at start (`--prepare`, and `--read` of a blank picture for the text recognizer), and meanwhile doesn't ask for a description and reads text with the built-in OCR.
- Phi Silica reports Ready on Kelly's PC, but using it is Access is denied ("Limited Access Feature is not available: com.microsoft.windows.ai.languagemodel"), so Check Windows AI says it isn't supported in Quick Describe yet. When it's about Microsoft's permission, the app's own wording stays neutral (Kelly, 17 Oct 2026).
- The camera (`Services/Camera.cs`) is on only while a picture is taken: `MediaCapture` with a frame reader, alone if it can be (to choose the largest size) or shared with an app already using it (also when it was had alone but sent no frame in 4 seconds: with the Camera app open, Windows grants it alone and sends nothing), and the frame kept once brightness stops changing (0.8 to 3 seconds), since a camera's first frames are dark. Only source groups with a colour picture count (not Windows Hello's infrared camera); back camera first, then the one facing the person, then ones Windows can't place, which include virtual cameras such as OBS's. One take at a time; if frames stop, the last one is used, or it gives up after 10 seconds. Every Windows camera call goes through `Camera.WithinAsync`, with a limit (60 seconds to start, which covers Windows' first permission question, and 15 for each later step) and Escape: a driver that never answers mustn't hold the camera, and a start given up on is disposed when it finishes. Settings saved before the camera give it M only with Ctrl+Alt+Shift held and M free (Ctrl+Alt is AltGr on many keyboards).
- `WindowsAI` takes an `IHelperConnection` factory, so the client's pairing, restarts, timeouts and cancellation are tested against `FakeHelper` (tests) without a real helper.

## Test builds in OneDrive

**Never sign on Kelly's PC, and never install signing tools there** (Kelly, 19 Nov 2026). Signed test builds come from CI: `gh workflow run release.yml --ref main` (or a branch) runs its "Installer (signed)" job, which signs with the Azure Artifact Signing certificate and keeps the result as the run's `installer` artifact, with no release (the `azure-signing` environment accepts any branch as well as `v*` tags; pull requests can't reach it). `gh run download <id> --name installer --dir <folder>`, then put the `.msixbundle` in `C:\Users\kelly\OneDrive\testing\quickdescribe\<version>\` with **Install Quick Describe.cmd**, `Install-FromFolder.ps1`, the Windows App Runtime packages, the release notes and a testing guide. Install with **Install Quick Describe.cmd** in that folder, not the .appinstaller: a `file:` .appinstaller names its packages by full path, and opening it failed ("can't open package") on Kelly's other PC (15 Oct 2026). The .cmd (`packaging/Install-FromFolder.ps1`) copies the package locally first and installs from there, from any path, which also makes OneDrive download online-only files. Copies installed that way don't update themselves; run it again for a newer build.

## Releasing

`VERSION` is the one version. Raise it in a PR, merge, then push `v<version>` on main: `.github/workflows/release.yml` builds and signs the installer into a **draft** release, with `docs/release-notes/v<version>.md` as its notes. Publishing waits for Kelly's go. Never publish a release without `QuickDescribe.appinstaller`: the latest release is where every installed copy looks for updates.

Signing needs, once per repo: the repo in `website/The-Idea-Place-Projects/signing/repos.json` with the `azure` group, `scripts/fix-federated-credentials.ps1` run, and `sync-secrets.py --apply` (see that folder's `windows.md`).

## Settings

`Views/SettingsWindow` edits a `HotKeyMap` (`Services/HotKeys.cs`: the modifiers, each command's letter or none, and `Problems()`, which refuses Shift alone and repeated letters) and the description type. OK applies the map through `MainWindow.UseHotKeys`, which re-registers them and returns the ones Windows refused; Settings says so once, and OK again keeps them. Cancel puts the earlier map back. `Settings.HotKeys` stores it; anything unusable reads as the defaults.

Start at sign-in is the package's startup task (`desktop:StartupTask` in `packaging/AppxManifest.xml`, `Services/StartAtSignIn.cs`), not a Run key: a packaged app's Run key writes go to its private registry, which Windows never reads at sign-in. It's Windows' state, read each time Settings opens, not kept in settings.json; turned off in Task Manager, only the person can turn it on again. Started that way (`StartedBySignIn`), `MainWindow.StartQuietly` keeps out of the way and holds the start's questions until `GreetIfQuiet`. Keep running (`Settings.KeepRunning`) shows `Views/TrayIcon` (WinForms' NotifyIcon, the app's one WinForms use: its implicit usings are removed in the .csproj, so write its types in full) and makes `OnClosing` hide the window unless `_exiting` is set (File > Exit, the icon's Exit, Windows signing out, an update).

Putting the description in (`Settings.AttachDescriptions`, on by default) writes `Services/EmbeddedDescription` into pictures saved: the same XMP fields as My Photo Library Described (dc:description, dc:title, IPTC alt text and extended description, its FileFields.cs rules for the 250-character alt text), as an iTXt chunk (PNG) or APP1 segment (JPEG) added to the file Quick Describe just encoded. Its own files only, so it's an insert, not My Photo Library Described's careful in-place editing, and a file that already has XMP is refused. Save Described Image writes it as it saves; save everything saves the copy as the picture is taken (kept even when describing fails) and adds the fields once the result is in (`AddDescriptionAsync`), logging a failure rather than saying it; a request cancelled before its result leaves its copy without. Save Described Image pressed before the result came gets it the same way (`_savedBeforeResult`). Adding them replaces a file written seconds before, when OneDrive or the indexer may hold it; Windows answers that with Access is denied (not a sharing violation), so `AddToFile` tries again twice, and `RemoveLeftovers` clears `.partial` files a quit cut short from Pictures\Quick Describe at start. File Explorer shows the Title for both formats (checked on Kelly's PC, 24 Nov 2026).

## Updates and bug reports

Copied from My Photo Library Described, The Idea Place apps' shared experience: `Services/Updates.cs` (Help > Check for Updates, the quiet daily check, "Update Installed" after an update; `MainWindow.Help.cs` wires them in), `Services/BugReport.cs` with `Views/BugReportWindow` (Help > Report a Bug… through the app kit's relay, app id `quickdescribe`; `CrashCheck` offers a report after a run that didn't quit cleanly), and `Services/Log.cs` (Help > Open Log Folder). The relay key reaches the package from the `APPKIT_RELAY_KEY` secret (release.yml) or `~/.signing-kit/appkit-relay-quickdescribe.key`; builds without it fall back to the GitHub page. Adding the app to the relay is in TheIdeaPlace/app-kit's `relay/README.md`.

## Accessibility

Every change is judged with JAWS, NVDA and Narrator. Results are announced with UIA notifications from the active window, the Result box is a read-only text box with a caret, and focus always lands in it. When a change needs a screen-reader check, list the exact steps in the PR.
