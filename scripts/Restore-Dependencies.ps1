param([switch]$Offline, [string]$CacheDirectory)
$ErrorActionPreference = 'Stop'
$rvRoot = Split-Path -Parent $PSScriptRoot
if (!$CacheDirectory) { $CacheDirectory = Join-Path $rvRoot '.cache' }
$rvLock = Get-Content -LiteralPath (Join-Path $rvRoot 'dependencies.lock.json') -Raw | ConvertFrom-Json
foreach ($rvItem in @($rvLock.compiler,$rvLock.framework,$rvLock.singbox,$rvLock.wintun,$rvLock.gitleaks)) {
    if ($rvItem.sha256 -cnotmatch '^[a-f0-9]{64}$') { throw 'Invalid pinned SHA256 value.' }
}
if ($rvLock.singbox.sourceSha256 -cnotmatch '^[a-f0-9]{64}$') { throw 'Invalid source archive SHA256 value.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
New-Item -ItemType Directory -Path $CacheDirectory,(Join-Path $rvRoot 'vendor') -Force | Out-Null
function Get-VerifiedFile($Item, [string]$Name) {
    $rvPath = Join-Path $CacheDirectory $Name
    if (!(Test-Path -LiteralPath $rvPath)) {
        if ($Offline) { throw "Missing pinned dependency: $Name" }
        $rvPending = $rvPath + '.download'
        try { Invoke-WebRequest -UseBasicParsing -Uri $Item.url -OutFile $rvPending; Move-Item -LiteralPath $rvPending -Destination $rvPath -Force }
        finally { if (Test-Path -LiteralPath $rvPending) { Remove-Item -LiteralPath $rvPending } }
    }
    if ((Get-FileHash -LiteralPath $rvPath -Algorithm SHA256).Hash -ne $Item.sha256) { throw "SHA256 mismatch: $Name" }
    return $rvPath
}
function Expand-VerifiedPackage($Item, [string]$Name, [string]$Folder) {
    $rvArchive = Get-VerifiedFile $Item $Name
    $rvDestination = Join-Path $CacheDirectory $Folder
    # Extract to a new directory on every restore. Do not trust stale extracted code.
    $rvCurrent = $rvDestination + '-' + [Guid]::NewGuid().ToString('N')
    [IO.Compression.ZipFile]::ExtractToDirectory($rvArchive, $rvCurrent)
    return $rvCurrent
}
$rvCompiler = Expand-VerifiedPackage $rvLock.compiler 'compiler.nupkg' 'compiler'
$rvFramework = Expand-VerifiedPackage $rvLock.framework 'framework.nupkg' 'framework'
$rvSing = Expand-VerifiedPackage $rvLock.singbox 'sing-box.zip' 'sing-box'
$rvWintun = Expand-VerifiedPackage $rvLock.wintun 'wintun.zip' 'wintun'
foreach ($rvPair in @(@($rvLock.singbox,$rvSing,'sing-box.exe'), @($rvLock.wintun,$rvWintun,'wintun.dll'))) {
    $rvSource = Join-Path $rvPair[1] $rvPair[0].file
    if ((Get-FileHash -LiteralPath $rvSource -Algorithm SHA256).Hash -ne $rvPair[0].fileSha256) { throw "Binary mismatch: $($rvPair[2])" }
    Copy-Item -LiteralPath $rvSource -Destination (Join-Path $rvRoot ('vendor/' + $rvPair[2])) -Force
}
Copy-Item -LiteralPath (Join-Path $rvRoot 'licenses/sing-box.txt') -Destination (Join-Path $rvRoot 'vendor/sing-box-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $rvRoot 'licenses/Wintun-binaries.txt') -Destination (Join-Path $rvRoot 'vendor/wintun-LICENSE.txt') -Force
@{ Compiler=(Join-Path $rvCompiler 'tasks/net472/csc.exe'); References=(Join-Path $rvFramework 'build/.NETFramework/v4.8') } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $CacheDirectory 'toolchain.json') -Encoding UTF8
Write-Output 'Pinned compiler, reference assemblies and upstream binaries verified.'
