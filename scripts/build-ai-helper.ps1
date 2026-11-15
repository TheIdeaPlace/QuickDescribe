<#
.SYNOPSIS
Builds Quick Describe's Windows AI helper for one architecture and lays out its package.

.DESCRIPTION
Publishes QuickDescribeAI self-contained into out\ai\<arch>, then writes AppxManifest.xml there with
the architecture, version and publisher filled in, beside the logos. That folder is a complete
package layout:
  - in Developer Mode, -Register installs it straight from the folder, for development;
  - -Pack packs it into artifacts\QuickDescribeAI_<arch>.msix, which a release signs and
    ships beside Quick Describe.

.PARAMETER Arch
x64 or arm64. Defaults to this PC's architecture.

.PARAMETER Publisher
The package publisher. For a signed release it must equal the signing certificate's subject
exactly. The default is for unsigned development builds.

.PARAMETER Version
The version, as up to three numbers. Defaults to VERSION, Quick Describe's own version.

.PARAMETER Revision
The package version's fourth number. Windows won't install a package over one of the same
version with different contents, and every build's contents differ (the build stamps its
commit into them), so a release build passes a number unique to the build (the workflow's run
number). 0 to 65535.

.PARAMETER Register
Install the built layout for the current user (needs Developer Mode). The package then runs
from out\ai\<arch> itself and claims the quickdescribe-ai command. Run -Unregister when you
are done, and always before deleting this folder or the worktree it is in: a registration
whose folder is gone stays behind, keeps the command, and Windows AI then fails with "The
system cannot find the path specified" (this happened to IDT's helper in November 2026).

.PARAMETER Unregister
Remove development registrations of the helper (the ones -Register makes). Builds nothing.
An installed Quick Describe is left alone; if you removed it while working on the helper,
install it again afterwards so its helper answers quickdescribe-ai.

.PARAMETER Pack
Also pack the layout into artifacts\QuickDescribeAI_<arch>.msix.

.EXAMPLE
.\scripts\build-ai-helper.ps1 -Register
quickdescribe-ai --check

.EXAMPLE
.\scripts\build-ai-helper.ps1 -Unregister
#>
param(
    [ValidateSet('x64', 'arm64')]
    [string]$Arch = $(if ((Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment').PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }),
    [string]$Publisher = 'CN=Quick Describe Development',
    [string]$Version,
    [ValidateRange(0, 65535)]
    [int]$Revision = 0,
    [string]$Configuration = 'Release',
    [switch]$Register,
    [switch]$Unregister,
    [switch]$Pack
)
$ErrorActionPreference = 'Stop'

# The package name in src\QuickDescribeAI\AppxManifest.xml.
$packageName = 'TheIdeaPlace.QuickDescribe.WindowsAI'

if ($Unregister) {
    if ($Register -or $Pack) { throw '-Unregister builds nothing; use it on its own.' }
    # Development registrations only: -Register makes them in Developer Mode, from a folder.
    # An installed Quick Describe (TheIdeaPlace.QuickDescribe, signed, in WindowsApps) is left alone.
    $dev = @(Get-AppxPackage -Name $packageName | Where-Object { $_.IsDevelopmentMode })
    if (-not $dev) {
        Write-Host "No development registration of $packageName to remove."
    }
    foreach ($package in $dev) {
        $where = if ($package.InstallLocation) { $package.InstallLocation } else { '(its folder no longer exists)' }
        Remove-AppxPackage -Package $package.PackageFullName
        Write-Host "Removed the development registration $($package.PackageFullName) from $where"
    }
    if (Get-AppxPackage -Name 'TheIdeaPlace.QuickDescribe') {
        Write-Host 'quickdescribe-ai now runs the installed Quick Describe''s helper.'
    } else {
        Write-Host 'Quick Describe isn''t installed; install it again to get its helper back.'
    }
    return
}

$windows = Split-Path $PSScriptRoot -Parent
$helper = Join-Path $windows 'src\QuickDescribeAI'
$project = Join-Path $helper 'QuickDescribeAI.csproj'
$out = Join-Path $windows "out\ai\$Arch"
if (-not $Version) { $Version = (Get-Content -LiteralPath (Join-Path $windows 'VERSION') -Raw).Trim() }
# A package version is four numbers; VERSION has three.
if ($Version -notmatch '^\s*(\d+)(?:\.(\d+))?(?:\.(\d+))?') { throw "Version '$Version' doesn't start with a number." }
$numbers = @($Matches[1], $(if ($Matches[2]) { $Matches[2] } else { '0' }), $(if ($Matches[3]) { $Matches[3] } else { '0' }))
$assemblyVersion = $numbers -join '.'
$packageVersion = "$assemblyVersion.$Revision"

if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
dotnet publish $project -c $Configuration -r "win-$Arch" -o $out --nologo "-p:Version=$assemblyVersion"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $Arch." }

# The display name, from the one place the app keeps it (AppInfo.Name).
$appInfo = Get-Content -LiteralPath (Join-Path $windows 'src\QuickDescribe\AppInfo.cs') -Raw -Encoding UTF8
if ($appInfo -notmatch 'const string Name = "([^"]+)"') { throw "AppInfo.cs has no Name constant." }
$displayName = [Security.SecurityElement]::Escape($Matches[1])
$manifest = (Get-Content -LiteralPath (Join-Path $helper 'AppxManifest.xml') -Raw).
    Replace('$ARCH$', $Arch).
    Replace('$VERSION$', $packageVersion).
    Replace('$PUBLISHER$', [Security.SecurityElement]::Escape($Publisher)).
    Replace('$DISPLAYNAME$', $displayName)
Set-Content -LiteralPath (Join-Path $out 'AppxManifest.xml') -Value $manifest -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $helper 'Assets') -Destination (Join-Path $out 'Assets') -Recurse -Force
# Debug symbols are for development, not for every installed copy.
Get-ChildItem -LiteralPath $out -Filter *.pdb | Remove-Item -Force

Write-Host "Built the Windows AI helper $packageVersion for $Arch in $out"

if ($Pack) {
    # makeappx comes with the Windows SDK build tools package, which restoring the project has
    # just put in the NuGet cache, so it is there on any machine that got this far, CI included.
    $packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
    # This process's own architecture, so the makeappx it starts runs natively or emulated alike.
    $hostArch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
    $makeappx = Get-ChildItem (Join-Path $packages 'microsoft.windows.sdk.buildtools') -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq $hostArch } | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $makeappx) { throw "makeappx.exe isn't in the NuGet cache ($packages); restore the project first." }
    $dist = Join-Path $windows 'artifacts'
    New-Item -ItemType Directory -Force -Path $dist | Out-Null
    $msix = Join-Path $dist "QuickDescribeAI_$Arch.msix"
    # makeappx lists every file it packs; only worth showing when it fails.
    $packLog = & $makeappx.FullName pack /d $out /p $msix /o /nv 2>&1
    if ($LASTEXITCODE -ne 0) { $packLog | Write-Host; throw "makeappx couldn't pack $out." }
    Write-Host "Packed $msix (unsigned: a release signs it)"
}

if ($Register) {
    Add-AppxPackage -Register (Join-Path $out 'AppxManifest.xml') -ForceUpdateFromAnyVersion
    Write-Host 'Registered for this user. Try: quickdescribe-ai --check'
    Write-Warning ("$out is now registered as the Windows AI helper and has the quickdescribe-ai " +
        "command. Run .\scripts\build-ai-helper.ps1 -Unregister when you are done, and before " +
        "deleting this folder or its worktree; otherwise Quick Describe's Windows AI stops working.")
}
