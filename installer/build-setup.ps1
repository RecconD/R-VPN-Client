param([string]$Archive, [Parameter(Mandatory=$true)][string]$OutputDirectory, [switch]$UninstallerOnly)
$ErrorActionPreference = 'Stop'
$rvRoot = Split-Path -Parent $PSScriptRoot
$rvToolchain = Get-Content -LiteralPath (Join-Path $rvRoot '.cache/toolchain.json') -Raw | ConvertFrom-Json
$rvOutput = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $rvOutput -Force | Out-Null
$rvArgs = @('/nologo','/noconfig','/nostdlib+','/target:winexe','/platform:x64','/langversion:5','/optimize+',
    '/deterministic+','/debug-','/utf8output',('/pathmap:' + $rvRoot + '=/src/rvpn'),
    ('/resource:' + (Join-Path $rvRoot 'assets/logo-original.svg') + ',logo.svg'),
    ('/resource:' + (Join-Path $rvRoot 'docs/PRIVACY.md') + ',PRIVACY.md'),
    ('/win32icon:' + (Join-Path $rvRoot 'assets/RVPN.ico')))
foreach ($rvReference in @('mscorlib.dll','System.dll','System.Core.dll','System.Xml.dll','Microsoft.CSharp.dll','System.Windows.Forms.dll',
    'System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','System.Xaml.dll','WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll')) {
    $rvArgs += '/reference:' + (Join-Path $rvToolchain.References $rvReference)
}
$rvSources = @((Join-Path $PSScriptRoot 'Setup.cs'),(Join-Path $rvRoot 'src/BrandDialog.cs'),
    (Join-Path $rvRoot 'src/WindowsShell.cs'),(Join-Path $rvRoot 'src/AssemblyInfo.cs'))
$rvUninstall = Join-Path $rvOutput 'R VPN Uninstall.exe'
if ($UninstallerOnly) {
    & $rvToolchain.Compiler @rvArgs '/define:UNINSTALL' ('/out:' + $rvUninstall) @rvSources
    if ($LASTEXITCODE -ne 0) { throw 'Uninstaller build failed.' }
    return
}
if (!(Test-Path -LiteralPath $rvUninstall)) { throw 'Build and (for a signed release) sign the uninstaller first.' }
$rvArchive = (Resolve-Path -LiteralPath $Archive).Path
$rvMatch = [regex]::Match([IO.Path]::GetFileName($rvArchive), '^R-VPN-Windows-x64-(\d+\.\d+\.\d+)\.zip$')
if (!$rvMatch.Success) { throw 'Unexpected package filename.' }
$rvSetup = Join-Path $rvOutput ('R-VPN-Setup-' + $rvMatch.Groups[1].Value + '.exe')
& $rvToolchain.Compiler @rvArgs ('/out:' + $rvSetup) ('/resource:' + $rvArchive + ',app.zip') ('/resource:' + $rvUninstall + ',uninstall.exe') @rvSources
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
Write-Output ('Installer built: ' + $rvSetup)
