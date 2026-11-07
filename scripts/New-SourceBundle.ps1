param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$rvRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'Test-PublicTree.ps1')
$rvOutput = [IO.Path]::GetFullPath($OutputDirectory)
$rvStage = Join-Path $rvOutput ('source-stage-' + [Guid]::NewGuid().ToString('N'))
$rvOwn = Join-Path $rvStage 'client'
New-Item -ItemType Directory -Path $rvOwn,(Join-Path $rvStage 'upstream/go-modules') -Force | Out-Null
foreach ($rvFile in @(git -C $rvRoot ls-files)) {
    $rvDestination = Join-Path $rvOwn $rvFile
    New-Item -ItemType Directory -Path (Split-Path -Parent $rvDestination) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $rvRoot $rvFile) -Destination $rvDestination
}
$rvLock = Get-Content -LiteralPath (Join-Path $rvRoot 'dependencies.lock.json') -Raw | ConvertFrom-Json
$rvUpstream = Join-Path $rvRoot '.cache/sing-box-source.tar.gz'
if (!(Test-Path -LiteralPath $rvUpstream)) { Invoke-WebRequest -UseBasicParsing -Uri $rvLock.singbox.sourceUrl -OutFile $rvUpstream }
if ((Get-FileHash -LiteralPath $rvUpstream).Hash -ne $rvLock.singbox.sourceSha256) { throw 'Upstream source archive checksum mismatch.' }
Copy-Item -LiteralPath $rvUpstream -Destination (Join-Path $rvStage 'upstream/sing-box-source.tar.gz')
$rvAudit = Join-Path $rvRoot 'build/module-license-audit.json'
if (!(Test-Path -LiteralPath $rvAudit)) { throw 'Run Get-GoModuleSources.ps1 before making a source bundle.' }
[object[]]$rvModules = Get-Content -LiteralPath $rvAudit -Raw | ConvertFrom-Json
[object[]]$rvExpected = Get-Content -LiteralPath (Join-Path $rvRoot 'docs/sing-box-modules.json') -Raw | ConvertFrom-Json
if ($rvModules.Count -ne $rvExpected.Count -or @($rvModules | Where-Object { !$_.verified -or $_.error }).Count) { throw 'Incomplete corresponding module source audit.' }
foreach ($rvModule in $rvModules) {
    if (!($rvExpected | Where-Object { $_.path -ceq $rvModule.path -and $_.version -ceq $rvModule.version -and $_.sum -ceq $rvModule.sum })) { throw 'Module inventory changed after source verification.' }
    $rvModuleArchive = Join-Path $rvRoot ('.cache/go-module-sources/' + $rvModule.archive)
    if (!$rvModule.archiveSha256 -or (Get-FileHash -LiteralPath $rvModuleArchive -Algorithm SHA256).Hash -ne $rvModule.archiveSha256) { throw 'Module archive changed after h1 verification.' }
    Copy-Item -LiteralPath $rvModuleArchive -Destination (Join-Path $rvStage ('upstream/go-modules/' + $rvModule.archive))
}
Copy-Item -LiteralPath $rvAudit -Destination (Join-Path $rvStage 'upstream/module-license-audit.json')
@'
This bundle contains the exact client source, sing-box snapshot and Go module
archives recorded by the official sing-box executable. Go h1 hashes are recorded
in the audit report. The official executable is restored by SHA-256, not rebuilt
or signed as R VPN code. Native prebuilt payloads in upstream module archives
retain their upstream license and may need additional corresponding-source
documentation. Read client/docs/SIGNPATH-ELIGIBILITY.md before publication.
Wintun's signed prebuilt DLL has a separate binary redistribution license.
'@ | Set-Content -LiteralPath (Join-Path $rvStage 'SOURCE-CONTENTS.txt') -Encoding UTF8
& (Join-Path $PSScriptRoot 'New-Archive.ps1') -Directory $rvStage -Archive (Join-Path $rvOutput 'R-VPN-corresponding-source.zip')
