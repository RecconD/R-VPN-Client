param([Parameter(Mandatory=$true)][string[]]$Path, [string]$Publisher = 'SignPath Foundation', [switch]$RequireSignTool)
$ErrorActionPreference = 'Stop'
$rvSignTool = Get-ChildItem -Path "${env:ProgramFiles(x86)}/Windows Kits/10/bin/*/x64/signtool.exe" -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
if ($RequireSignTool -and !$rvSignTool) { throw 'Windows SDK SignTool is required for a signed release.' }
$rvResults = foreach ($rvFile in $Path) {
    $rvResolved = (Resolve-Path -LiteralPath $rvFile).Path
    $rvSignature = Get-AuthenticodeSignature -LiteralPath $rvResolved
    if ($rvSignature.Status -ne 'Valid') { throw ('Authenticode validation failed: ' + [IO.Path]::GetFileName($rvFile) + ' = ' + $rvSignature.Status) }
    $rvCertificate = $rvSignature.SignerCertificate
    if ($rvCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName,$false) -cne $Publisher) {
        throw ('Unexpected signing publisher: ' + $rvCertificate.Subject)
    }
    if (!$rvSignature.TimeStamperCertificate) { throw ('Missing trusted timestamp: ' + $rvFile) }
    $rvEkus = @($rvCertificate.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' } | ForEach-Object { $_.EnhancedKeyUsages | ForEach-Object Value })
    if ($rvEkus -notcontains '1.3.6.1.5.5.7.3.3') { throw 'Certificate is not valid for code signing.' }
    if ($rvSignTool) {
        & $rvSignTool verify /pa /all /tw /v $rvResolved
        if ($LASTEXITCODE -ne 0) { throw ('SignTool verification failed: ' + $rvFile) }
    }
    [pscustomobject]@{File=[IO.Path]::GetFileName($rvFile);Status='Valid';Publisher=$rvCertificate.Subject;
        Thumbprint=$rvCertificate.Thumbprint;TimestampIssuer=$rvSignature.TimeStamperCertificate.Subject;
        SHA256=(Get-FileHash -LiteralPath $rvResolved -Algorithm SHA256).Hash}
}
$rvResults
