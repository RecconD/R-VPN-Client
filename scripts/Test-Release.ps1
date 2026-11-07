param([Parameter(Mandatory=$true)][string]$Directory, [switch]$RequireSigned)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$rvRoot = Split-Path -Parent $PSScriptRoot
$rvDirectory = (Resolve-Path -LiteralPath $Directory).Path
$rvSetup = @(Get-ChildItem -LiteralPath $rvDirectory -Filter 'R-VPN-Setup-*.exe')
$rvArchive = @(Get-ChildItem -LiteralPath $rvDirectory -Filter 'R-VPN-Windows-x64-*.zip')
if ($rvSetup.Count -ne 1 -or $rvArchive.Count -ne 1) { throw 'Expected one setup and one portable ZIP.' }
$rvExtract = Join-Path $rvDirectory ('verification-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $rvExtract | Out-Null
# Reflection-only loading reads CLR resources without executing installer code.
$rvAssembly = [Reflection.Assembly]::ReflectionOnlyLoad([IO.File]::ReadAllBytes($rvSetup[0].FullName))
foreach ($rvName in @('app.zip','uninstall.exe')) {
    $rvInput = $rvAssembly.GetManifestResourceStream($rvName)
    if (!$rvInput) { throw ('Missing installer resource: ' + $rvName) }
    $rvOutput = [IO.File]::Create((Join-Path $rvExtract $rvName))
    try { $rvInput.CopyTo($rvOutput) } finally { $rvInput.Dispose(); $rvOutput.Dispose() }
}
if ((Get-FileHash -LiteralPath (Join-Path $rvExtract 'app.zip')).Hash -ne (Get-FileHash -LiteralPath $rvArchive[0].FullName).Hash) { throw 'Embedded ZIP differs from the released ZIP.' }
if ((Get-FileHash -LiteralPath (Join-Path $rvExtract 'uninstall.exe')).Hash -ne (Get-FileHash -LiteralPath (Join-Path $rvDirectory 'R VPN Uninstall.exe')).Hash) { throw 'Embedded uninstaller differs.' }
[IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $rvExtract 'app.zip'),(Join-Path $rvExtract 'payload'))
$rvPayload = Join-Path $rvExtract 'payload/R VPN'
$rvLock = Get-Content -LiteralPath (Join-Path $rvRoot 'dependencies.lock.json') -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath (Join-Path $rvPayload 'core/sing-box.exe')).Hash -ne $rvLock.singbox.fileSha256 -or
    (Get-FileHash -LiteralPath (Join-Path $rvPayload 'core/wintun.dll')).Hash -ne $rvLock.wintun.fileSha256) { throw 'Upstream dependency was modified.' }
$rvOwn = @($rvSetup[0].FullName,(Join-Path $rvPayload 'R VPN.exe'),(Join-Path $rvPayload 'core/RVPN.EngineHost.exe'),(Join-Path $rvExtract 'uninstall.exe'))
if ($RequireSigned) { & (Join-Path $PSScriptRoot 'Assert-Signatures.ps1') -Path $rvOwn -RequireSignTool }
else { $rvOwn | ForEach-Object { $rvSig=Get-AuthenticodeSignature -LiteralPath $_; [pscustomobject]@{File=[IO.Path]::GetFileName($_);Status=[string]$rvSig.Status} } }
$rvReadme = [IO.File]::ReadAllText((Join-Path $rvPayload 'README.md'))
foreach ($rvName in @('docs/PRIVACY.md','THIRD-PARTY-NOTICES.md','LICENSE','licenses/sing-box.txt',
    'licenses/Wintun-binaries.txt','licenses/Simple-Icons-CC0.txt','licenses/sing-box-dependencies.txt','assets/fonts/Manrope-OFL.txt')) {
    $rvLegal = [IO.File]::ReadAllText((Join-Path $rvRoot $rvName)).Replace("`r`n","`n")
    if (!$rvReadme.Contains($rvLegal)) { throw ('Missing complete legal text in package README: ' + $rvName) }
}
Write-Output 'Installer payload, upstream hashes and license documents verified without executing setup.'
