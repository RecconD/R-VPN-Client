param([switch]$RunGitleaks)
$ErrorActionPreference = 'Stop'
$rvRoot = Split-Path -Parent $PSScriptRoot
$rvFiles = @(git -C $rvRoot ls-files)
if ($LASTEXITCODE -ne 0 -or !$rvFiles.Count) { throw 'Initialize and stage the independent client repository before running the public-tree audit.' }
$rvBadPaths = @($rvFiles | Where-Object { $_ -match '(^|/)(server|admin|android|ios|vendor|build|qa|\.cache|\.aws|\.ssh)(/|$)|\.(pem|pfx|p12|key|sqlite3?|db|log|exe|dll|zip)$|(^|/)(\.env.*|state\.dat|profile\.json|privacy\.json)$' })
if ($rvBadPaths.Count) { throw ('Forbidden tracked paths: ' + ($rvBadPaths -join ', ')) }
$rvFindings = @()
foreach ($rvFile in $rvFiles) {
    if ($rvFile -match '\.(ttf|ico|gz)$') { continue }
    $rvText = [IO.File]::ReadAllText((Join-Path $rvRoot $rvFile))
    $rvPrivateXml = '<RSAKey' + 'Value>[\s\S]*<D>'
    if ($rvText -match '-----BEGIN (RSA |EC |OPENSSH |ENCRYPTED )?PRIVATE KEY-----' -or $rvText -match $rvPrivateXml) { $rvFindings += $rvFile + ':private-key' }
    if ($rvText -match '(?i)(password|passwd|accessToken|api[_-]?key)\s*[:=]\s*["''][^"''\s]{12,}["'']') { $rvFindings += $rvFile + ':credential-literal' }
    foreach ($rvProfile in [regex]::Matches($rvText, 'vless://([a-fA-F0-9-]{36})@')) {
        if ($rvProfile.Groups[1].Value -cne '00000000-0000-4000-8000-000000000001' -or $rvFile -notin @('src/DesktopQa.cs','src/EngineController.cs')) {
            $rvFindings += $rvFile + ':vpn-profile'
        }
    }
}
if ($rvFindings.Count) { throw ('Review required (values redacted): ' + ($rvFindings -join ', ')) }
if ($RunGitleaks) {
    $rvLock = Get-Content -LiteralPath (Join-Path $rvRoot 'dependencies.lock.json') -Raw | ConvertFrom-Json
    $rvArchive = Join-Path $rvRoot '.cache/gitleaks.zip'
    New-Item -ItemType Directory -Path (Split-Path -Parent $rvArchive) -Force | Out-Null
    if (!(Test-Path -LiteralPath $rvArchive)) { Invoke-WebRequest -UseBasicParsing -Uri $rvLock.gitleaks.url -OutFile $rvArchive }
    if ((Get-FileHash -LiteralPath $rvArchive).Hash -ne $rvLock.gitleaks.sha256) { throw 'Gitleaks checksum mismatch.' }
    $rvScanner = Join-Path $rvRoot ('.cache/gitleaks-' + [Guid]::NewGuid().ToString('N'))
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($rvArchive,$rvScanner)
    $rvReport = Join-Path $rvRoot 'build/gitleaks.json'
    New-Item -ItemType Directory -Path (Split-Path -Parent $rvReport) -Force | Out-Null
    # Audit exactly the files proposed for publication. Build caches contain
    # upstream scanner examples which deliberately resemble test credentials.
    $rvScanTree = Join-Path $rvRoot ('.cache/public-scan-' + [Guid]::NewGuid().ToString('N'))
    foreach ($rvFile in $rvFiles) {
        $rvTarget = Join-Path $rvScanTree $rvFile
        New-Item -ItemType Directory -Path (Split-Path -Parent $rvTarget) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $rvRoot $rvFile) -Destination $rvTarget
    }
    & (Join-Path $rvScanner 'gitleaks.exe') dir $rvScanTree --redact=100 --no-banner --report-format json --report-path $rvReport
    if ($LASTEXITCODE -ne 0) { throw 'Gitleaks reported a finding. Review the redacted report before publication.' }
    # Once commit history exists, scan the history as well as the current tree.
    git -C $rvRoot rev-parse --verify HEAD 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) {
        & (Join-Path $rvScanner 'gitleaks.exe') git $rvRoot --redact=100 --no-banner --report-format json --report-path (Join-Path $rvRoot 'build/gitleaks-history.json')
        if ($LASTEXITCODE -ne 0) { throw 'Gitleaks found a history issue.' }
    }
}
Write-Output ('Public source inventory passed: ' + $rvFiles.Count + ' tracked files. No private server tree or credentials detected.')
