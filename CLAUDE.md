# CLAUDE.md

Quick Describe: a small Windows app that describes the screen, the active window, the focused item, a clipboard picture or a picture file, with Windows' on-device AI, and reads the text in it. Kept deliberately simple (Kelly, 15 Oct 2026): on-device only, no cloud providers, no settings beyond the description type.

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

`QD_SCREEN_TRIAL=1` runs `ScreenTrialTests`, which captures this PC's real screen, window and focused item and prints only outcomes and timings.

Windows AI needs the Copilot+ NPU, which the vmtest VMs don't have, so this app is tested on Kelly's PC (Kelly, 15 Oct 2026). Keep such runs short and close what you open.

## Things learned the hard way

- Windows AI's `ImageDescriptionGenerator` refuses most screens and windows with `ImageHasTooMuchText` (WindowsAppSDK #6855). Shrinking the picture until it accepts (about 256 pixels) gets descriptions that invent text ("CiWsers Veel"), so Quick Describe doesn't; it shows the text instead.
- Windows AI's `TextRecognizer` reads screens far better than `Windows.Media.Ocr` (it keeps the "4" of "42 Harbor Road" beside a text box border, which the older OCR drops at every scale). The app uses it through the helper and falls back to `Windows.Media.Ocr` on PCs without it.
- The focused item's area is sized in 100%-scaling pixels and multiplied by the monitor's scale; a fixed pixel size cut labels in half at 200%.

## Releasing

`VERSION` is the one version. Raise it in a PR, merge, then push `v<version>` on main: `.github/workflows/release.yml` builds and signs the installer into a **draft** release, with `docs/release-notes/v<version>.md` as its notes. Publishing waits for Kelly's go. Never publish a release without `QuickDescribe.appinstaller`: the latest release is where every installed copy looks for updates.

Signing needs, once per repo: the repo in `website/The-Idea-Place-Projects/signing/repos.json` with the `azure` group, `scripts/fix-federated-credentials.ps1` run, and `sync-secrets.py --apply` (see that folder's `windows.md`).

## Accessibility

Every change is judged with JAWS, NVDA and Narrator. Results are announced with UIA notifications from the active window, the Result box is a read-only text box with a caret, and focus always lands in it. When a change needs a screen-reader check, list the exact steps in the PR.
