# CLAUDE.md

Quick Describe: a small Windows app that describes the screen, the active window, the focused item, a clipboard picture or a picture file, with Windows' on-device AI, and reads the text in it. Kept deliberately simple (Kelly, 15 Oct 2026): on-device only, no cloud providers, few settings: the hotkeys, the description type, starting at sign-in and keeping running when closed.

## Layout

- `src/QuickDescribe` — the WPF app. `Services/` holds everything testable: capture geometry (`PixelRect`), capture (`ScreenCapture`), pictures and clipboard (`Pictures`), text (`TextRecognition`), the helper client (`WindowsAI`), putting a result together (`Result`, `Describing`), hotkeys, settings, announcements. `Views/MainWindow` is the only window.
- `src/QuickDescribeAI` — the packaged Windows AI helper, adapted from My Photo Library Described's `PhotoFinderAI` (same JSON-lines protocol, plus `"op": "read"` for `TextRecognizer`). Windows only lets a packaged app use its AI models; the app starts this from its package's `AI\` folder, or in development through the `quickdescribe-ai.exe` alias a registered helper adds.
- `tests/QuickDescribe.Tests`, `tests/QuickDescribeAI.Tests` — no test opens a window. Tests that need Windows AI skip themselves unless the helper is installed and the model is ready.
- `packaging/`, `scripts/build-msix.ps1`, `scripts/sign-files.ps1` — the MSIX installer and signing, copied from My Photo Library Described.

## Building and testing

From PowerShell or cmd, never Git Bash (the helper answers every picture with internal_error when started from Git Bash):

```powershell
dotnet build QuickDescribe.sln
dotnet test --solution QuickDescribe.sln
.\scripts\build-ai-helper.ps1 -Register   # Developer Mode; Windows AI in development builds
```

After changing the helper (`src/QuickDescribeAI`), run `.\scripts\build-ai-helper.ps1 -Register` again: development builds and the live tests use the registered helper, not the one just built. An installed Quick Describe claims the same `quickdescribe-ai` command, so remove it (`Get-AppxPackage TheIdeaPlace.QuickDescribe | Remove-AppxPackage`) while working on the helper.

Drawn text comes out blank when Windows' session has no display (Kelly's remote session disconnected), so the tests that draw text fail on this PC then; CI runs them.

`QD_SCREEN_TRIAL=1` runs `ScreenTrialTests`, which captures this PC's real screen, window and focused item and prints only outcomes and timings.

Windows AI needs the Copilot+ NPU, which the vmtest VMs don't have, so this app is tested on Kelly's PC (Kelly, 15 Oct 2026). Keep such runs short and close what you open.

## Things learned the hard way

- Windows AI's `ImageDescriptionGenerator` refuses most screens and windows with `ImageHasTooMuchText` (WindowsAppSDK #6855). Shrinking the picture until it accepts (about 256 pixels) gets descriptions that invent text ("CiWsers Veel"), so Quick Describe doesn't; it shows the text instead.
- Windows AI's `TextRecognizer` reads screens far better than `Windows.Media.Ocr` (it keeps the "4" of "42 Harbor Road" beside a text box border, which the older OCR drops at every scale). The app uses it through the helper and falls back to `Windows.Media.Ocr` on PCs without it.
- The focused item's area is sized in 100%-scaling pixels and multiplied by the monitor's scale; a fixed pixel size cut labels in half at 200%.
- `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` keeps the window out of a GDI `BitBlt` of the screen too (checked on Kelly's PC), so a screen taken while Quick Describe shows doesn't read its own last result back. It's set only around the capture: left on, it hides the window from screen sharing, screenshots and perhaps magnifiers.
- `Requests` owns describing's sequencing: a new request or Escape cancels the last; messages (F1, Help) never do, or a result on its way is lost.
- `WindowsAI` takes an `IHelperConnection` factory, so the client's pairing, restarts, timeouts and cancellation are tested against `FakeHelper` (tests) without a real helper.

## Test builds in OneDrive

`.\scripts\build-msix.ps1 -Sign -Destination C:\Users\kelly\OneDrive\qdescribe` builds Kelly's test copy. Install with **Install Quick Describe.cmd** in that folder, not the .appinstaller: a `file:` .appinstaller names its packages by full path, and opening it failed ("can't open package") on Kelly's other PC (15 Oct 2026). The .cmd (`packaging/Install-FromFolder.ps1`) copies the package locally first and installs from there, from any path, which also makes OneDrive download online-only files. Copies installed that way don't update themselves; run it again for a newer build.

## Releasing

`VERSION` is the one version. Raise it in a PR, merge, then push `v<version>` on main: `.github/workflows/release.yml` builds and signs the installer into a **draft** release, with `docs/release-notes/v<version>.md` as its notes. Publishing waits for Kelly's go. Never publish a release without `QuickDescribe.appinstaller`: the latest release is where every installed copy looks for updates.

Signing needs, once per repo: the repo in `website/The-Idea-Place-Projects/signing/repos.json` with the `azure` group, `scripts/fix-federated-credentials.ps1` run, and `sync-secrets.py --apply` (see that folder's `windows.md`).

## Settings

`Views/SettingsWindow` edits a `HotKeyMap` (`Services/HotKeys.cs`: the modifiers, each command's letter or none, and `Problems()`, which refuses Shift alone and repeated letters) and the description type. OK applies the map through `MainWindow.UseHotKeys`, which re-registers them and returns the ones Windows refused; Settings says so once, and OK again keeps them. Cancel puts the earlier map back. `Settings.HotKeys` stores it; anything unusable reads as the defaults.

Start at sign-in is the package's startup task (`desktop:StartupTask` in `packaging/AppxManifest.xml`, `Services/StartAtSignIn.cs`), not a Run key: a packaged app's Run key writes go to its private registry, which Windows never reads at sign-in. It's Windows' state, read each time Settings opens, not kept in settings.json; turned off in Task Manager, only the person can turn it on again. Started that way (`StartedBySignIn`), `MainWindow.StartQuietly` keeps out of the way and holds the start's questions until `GreetIfQuiet`. Keep running (`Settings.KeepRunning`) shows `Views/TrayIcon` (WinForms' NotifyIcon, the app's one WinForms use: its implicit usings are removed in the .csproj, so write its types in full) and makes `OnClosing` hide the window unless `_exiting` is set (File > Exit, the icon's Exit, Windows signing out, an update).

## Updates and bug reports

Copied from My Photo Library Described, The Idea Place apps' shared experience: `Services/Updates.cs` (Help > Check for Updates, the quiet daily check, "Update Installed" after an update; `MainWindow.Help.cs` wires them in), `Services/BugReport.cs` with `Views/BugReportWindow` (Help > Report a Bug… through the app kit's relay, app id `quickdescribe`; `CrashCheck` offers a report after a run that didn't quit cleanly), and `Services/Log.cs` (Help > Open Log Folder). The relay key reaches the package from the `APPKIT_RELAY_KEY` secret (release.yml) or `~/.signing-kit/appkit-relay-quickdescribe.key`; builds without it fall back to the GitHub page. Adding the app to the relay is in TheIdeaPlace/app-kit's `relay/README.md`.

## Accessibility

Every change is judged with JAWS, NVDA and Narrator. Results are announced with UIA notifications from the active window, the Result box is a read-only text box with a caret, and focus always lands in it. When a change needs a screen-reader check, list the exact steps in the PR.
