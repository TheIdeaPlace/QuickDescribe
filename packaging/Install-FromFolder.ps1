<#
Installs Quick Describe from the folder this script is in, wherever that folder is: a OneDrive folder synced to any PC,
a USB stick, a network share. scripts\build-msix.ps1 puts it, and "Install Quick Describe.cmd" that runs it, beside a
build made for a folder rather than a website.

Why not just open QuickDescribe.appinstaller? That file has to name its packages by full path, so it only works where
the folder is at exactly the same path as on the PC that built it. And Windows installs through a system service, which
can't make OneDrive download a file that's online-only. This copies the package and the Windows App Runtime to a local
folder first (which downloads them, as the person running it) and installs from there. Updates aren't automatic from a
copy installed this way: run it again for a newer build.

PowerShell 5.1 compatible.
#>
$ErrorActionPreference = 'Stop'
try {
    $here = Split-Path -Parent $MyInvocation.MyCommand.Path
    $bundle = Get-ChildItem -LiteralPath $here -Filter 'QuickDescribe_*.msixbundle' | Sort-Object Name -Descending | Select-Object -First 1
    if (-not $bundle) { throw "There's no Quick Describe package (QuickDescribe_*.msixbundle) in $here." }

    # The PC's own architecture, even from a 32-bit or emulated PowerShell.
    $arch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64' -or $env:PROCESSOR_ARCHITEW6432 -eq 'ARM64') { 'arm64' } else { 'x64' }
    $runtime = Get-ChildItem -LiteralPath $here -Recurse -Filter "Microsoft.WindowsAppRuntime.1.8_$arch.msix" | Select-Object -First 1
    if (-not $runtime) { throw "The Windows App Runtime for $arch (Microsoft.WindowsAppRuntime.1.8_$arch.msix) isn't in $here." }

    $local = Join-Path $env:TEMP 'QuickDescribeInstall'
    if (Test-Path -LiteralPath $local) { Remove-Item -LiteralPath $local -Recurse -Force }
    New-Item -ItemType Directory -Path $local | Out-Null
    Write-Host "Copying Quick Describe to this PC. If OneDrive hasn't downloaded it yet, that happens now, which can take a minute."
    Copy-Item -LiteralPath $bundle.FullName -Destination $local
    Copy-Item -LiteralPath $runtime.FullName -Destination $local

    Write-Host "Installing $($bundle.BaseName -replace '^QuickDescribe_', 'Quick Describe ') for $arch."
    Add-AppxPackage -Path (Join-Path $local $bundle.Name) -DependencyPath (Join-Path $local $runtime.Name) -ForceUpdateFromAnyVersion
    Remove-Item -LiteralPath $local -Recurse -Force -ErrorAction SilentlyContinue

    $installed = Get-AppxPackage TheIdeaPlace.QuickDescribe
    Write-Host "Done: Quick Describe $($installed.Version) is installed. It's in the Start menu."
    exit 0
}
catch {
    Write-Host ''
    Write-Host "Quick Describe couldn't be installed: $($_.Exception.Message.Trim())"
    exit 1
}
