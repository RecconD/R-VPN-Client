param([switch]$Offline)
# Requires PowerShell 7 for parallel downloads. No code from modules is executed.
$ErrorActionPreference = 'Stop'
$rvRoot = Split-Path -Parent $PSScriptRoot
$rvModules = @(Get-Content -LiteralPath (Join-Path $rvRoot 'docs/sing-box-modules.json') -Raw | ConvertFrom-Json)
$rvCache = Join-Path $rvRoot '.cache/go-module-sources'
New-Item -ItemType Directory -Path $rvCache -Force | Out-Null
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'Source audit requires PowerShell 7. The client build uses Windows PowerShell 5.1.' }
$rvDownloads = $rvModules | ForEach-Object -ThrottleLimit 6 -Parallel {
    $ErrorActionPreference = 'Stop'
    $rvModule = $_
    $rvEscape = { param($value) [regex]::Replace($value, '[A-Z]', { param($m) '!' + $m.Value.ToLowerInvariant() }) }
    $rvEscaped = (& $rvEscape $rvModule.path) + '/@v/' + (& $rvEscape $rvModule.version) + '.zip'
    $rvHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($rvEscaped))).ToLowerInvariant()
    $rvArchive = Join-Path $using:rvCache ($rvHash + '.zip')
    try {
        if (!(Test-Path -LiteralPath $rvArchive)) {
            if ($using:Offline) { throw 'Not cached' }
            Invoke-WebRequest -Uri ('https://proxy.golang.org/' + $rvEscaped) -OutFile ($rvArchive + '.download') -TimeoutSec 90
            Move-Item -LiteralPath ($rvArchive + '.download') -Destination $rvArchive
        }
        [pscustomobject]@{path=$rvModule.path;version=$rvModule.version;sum=$rvModule.sum;archive=$rvHash+'.zip';error=$null}
    } catch { [pscustomobject]@{path=$rvModule.path;version=$rvModule.version;sum=$rvModule.sum;archive=$rvHash+'.zip';error=$_.Exception.Message} }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$rvReports = foreach ($rvModule in ($rvDownloads | Sort-Object path)) {
    if ($rvModule.error) { $rvModule; continue }
    $rvZip = [IO.Compression.ZipFile]::OpenRead((Join-Path $rvCache $rvModule.archive))
    try {
        $rvNames = [string[]]@($rvZip.Entries | Where-Object { $_.Name } | ForEach-Object FullName)
        [Array]::Sort($rvNames, [StringComparer]::Ordinal)
        $rvIndex = @{}; foreach ($rvEntry in $rvZip.Entries) { $rvIndex[$rvEntry.FullName] = $rvEntry }
        $rvHashes = [Text.StringBuilder]::new()
        $rvLicenses = @()
        foreach ($rvName in $rvNames) {
            $rvEntry = $rvIndex[$rvName]
            $rvStream = $rvEntry.Open()
            try { $rvSha = [Security.Cryptography.SHA256]::Create(); $rvDigest = [BitConverter]::ToString($rvSha.ComputeHash($rvStream)).Replace('-','').ToLowerInvariant() }
            finally { $rvStream.Dispose(); $rvSha.Dispose() }
            [void]$rvHashes.Append($rvDigest + '  ' + $rvName + "`n")
            $rvRelative = $rvName.Substring(($rvModule.path + '@' + $rvModule.version + '/').Length)
            if ($rvRelative -match '(^|/)(LICENSE[^/]*|COPYING[^/]*|NOTICE[^/]*)$' -and $rvRelative -notmatch '\.(go|c|h|py|js)$') {
                $rvReader = [IO.StreamReader]::new($rvEntry.Open())
                try { $rvText = $rvReader.ReadToEnd() } finally { $rvReader.Dispose() }
                $rvLicense = if ($rvText -match 'GNU AFFERO GENERAL PUBLIC LICENSE') {'AGPL-3.0'}
                    elseif ($rvText -match 'GNU GENERAL PUBLIC LICENSE' -and $rvText -match 'Version 3,|either version 3 of the License') {'GPL-3.0-or-later'}
                    elseif ($rvText -match 'GNU GENERAL PUBLIC LICENSE' -and $rvText -match 'Version 2,') {'GPL-2.0'}
                    elseif ($rvText -match 'Mozilla Public License.*2\.0') {'MPL-2.0'}
                    elseif ($rvText -match 'Apache License' -and $rvText -match 'Version 2\.0') {'Apache-2.0'}
                    elseif ($rvText -match 'Permission is hereby granted, free of charge') {'MIT-like'}
                    elseif ($rvText -match 'Redistribution and use in source and binary forms') {'BSD-like'}
                    elseif ($rvText -match 'Permission to use, copy, modify, and(/or)? distribute this software') {'ISC-like'}
                    elseif ($rvText -match 'CC0|Creative Commons Zero') {'CC0-1.0'}
                    elseif ($rvText -match 'public domain') {'Public-domain-notice'}
                    else {'REVIEW-REQUIRED'}
                if ($rvText -match 'Additional (Permission|Terms)|no derivative work may use the name') { $rvLicense += '; upstream additional notice' }
                $rvLicenses += [pscustomobject]@{file=$rvRelative;classification=$rvLicense}
            }
        }
        $rvSha = [Security.Cryptography.SHA256]::Create()
        try { $rvH1 = 'h1:' + [Convert]::ToBase64String($rvSha.ComputeHash([Text.Encoding]::UTF8.GetBytes($rvHashes.ToString()))) }
        finally { $rvSha.Dispose() }
        if ($rvH1 -cne $rvModule.sum) { throw ('Go h1 mismatch: ' + $rvModule.path) }
        [pscustomobject]@{path=$rvModule.path;version=$rvModule.version;sum=$rvH1;archive=$rvModule.archive;
            archiveSha256=(Get-FileHash -LiteralPath (Join-Path $rvCache $rvModule.archive) -Algorithm SHA256).Hash.ToLowerInvariant();
            verified=$true;licenses=$rvLicenses;error=$null}
    } finally { $rvZip.Dispose() }
}
$rvReport = Join-Path $rvRoot 'build/module-license-audit.json'
New-Item -ItemType Directory -Path (Split-Path -Parent $rvReport) -Force | Out-Null
$rvReports | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $rvReport -Encoding UTF8
$rvFailed = @($rvReports | Where-Object { $_.error -or !$_.verified })
if ($rvFailed.Count) { throw ('Module source downloads failed: ' + $rvFailed.Count + '. Inspect ' + $rvReport) }
Write-Output ('Verified Go module source archives: ' + $rvReports.Count)
Write-Output ('License classifications are heuristic evidence, not an approval decision: ' + $rvReport)
