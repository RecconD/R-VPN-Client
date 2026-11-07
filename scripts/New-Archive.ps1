param([Parameter(Mandatory=$true)][string]$Directory, [Parameter(Mandatory=$true)][string]$Archive, [string]$Prefix = '')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$rvSource = (Resolve-Path -LiteralPath $Directory).Path.TrimEnd('\','/')
$rvTarget = [IO.Path]::GetFullPath($Archive)
if (Test-Path -LiteralPath $rvTarget) { throw "Refusing to overwrite archive: $rvTarget" }
New-Item -ItemType Directory -Path (Split-Path -Parent $rvTarget) -Force | Out-Null
$rvFile = [IO.File]::Open($rvTarget, [IO.FileMode]::CreateNew)
$rvZip = [IO.Compression.ZipArchive]::new($rvFile, [IO.Compression.ZipArchiveMode]::Create, $false)
try {
    $rvNames = [string[]](Get-ChildItem -LiteralPath $rvSource -File -Recurse | ForEach-Object { $_.FullName.Substring($rvSource.Length + 1).Replace('\','/') })
    [Array]::Sort($rvNames, [StringComparer]::Ordinal)
    foreach ($rvRelative in $rvNames) {
        $rvEntry = $rvZip.CreateEntry($Prefix + $rvRelative, [IO.Compression.CompressionLevel]::Optimal)
        $rvEntry.LastWriteTime = [DateTimeOffset]::new(2020,1,1,0,0,0,[TimeSpan]::Zero)
        $rvInput = [IO.File]::OpenRead((Join-Path $rvSource $rvRelative))
        $rvOutput = $rvEntry.Open()
        try { $rvInput.CopyTo($rvOutput) } finally { $rvInput.Dispose(); $rvOutput.Dispose() }
    }
} finally { $rvZip.Dispose(); $rvFile.Dispose() }
