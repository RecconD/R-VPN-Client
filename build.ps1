param([switch]$Test, [switch]$Package, [switch]$Offline, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$rvRoot = $PSScriptRoot
if (!$OutputDirectory) { $OutputDirectory = Join-Path $rvRoot 'build' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
& (Join-Path $rvRoot 'scripts/Restore-Dependencies.ps1') -Offline:$Offline
$rvToolchain = Get-Content -LiteralPath (Join-Path $rvRoot '.cache/toolchain.json') -Raw | ConvertFrom-Json
$rvCompiler = $rvToolchain.Compiler
$rvFramework = $rvToolchain.References
$rvVersion = [regex]::Match((Get-Content -LiteralPath (Join-Path $rvRoot 'src/AssemblyInfo.cs') -Raw), 'AssemblyVersion\("(\d+\.\d+\.\d+)\.0"\)').Groups[1].Value
if (!$rvVersion) { throw 'Missing release version.' }
$rvRelease = Join-Path $OutputDirectory 'R VPN'
if (Test-Path -LiteralPath $rvRelease) { throw 'Use a new output directory to avoid packaging stale files.' }
$rvCore = Join-Path $rvRelease 'core'
$rvFonts = Join-Path $rvRelease 'fonts'
New-Item -ItemType Directory -Path $rvRelease,$rvCore,$rvFonts -Force | Out-Null
$rvCommon = @('/nologo','/noconfig','/nostdlib+','/platform:x64','/langversion:5','/optimize+','/utf8output',
    '/warn:4','/deterministic+','/debug-',('/pathmap:' + $rvRoot + '=/src/rvpn'))
$rvReferences = @('mscorlib.dll','System.dll','System.Core.dll','System.Net.Http.dll','System.Security.dll',
    'System.Web.Extensions.dll','System.Xml.dll','System.Xaml.dll','System.IO.Compression.dll',
    'System.IO.Compression.FileSystem.dll','WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll',
    'UIAutomationProvider.dll','UIAutomationTypes.dll')
$rvArguments = $rvCommon + @('/target:winexe',('/out:' + (Join-Path $rvRelease 'R VPN.exe')))
foreach ($rvReference in $rvReferences) { $rvArguments += '/reference:' + (Join-Path $rvFramework $rvReference) }
$rvArguments += '/resource:' + (Join-Path $rvRoot 'src/Window.xaml') + ',Window.xaml'
$rvArguments += '/resource:' + (Join-Path $rvRoot 'assets/logo-original.svg') + ',logo.svg'
$rvIcon = Join-Path $rvRoot 'assets/RVPN.ico'
$rvArguments += '/win32icon:' + $rvIcon
$rvArguments += '/resource:' + $rvIcon + ',RVPN.ico'
$rvArguments += '/resource:' + (Join-Path $rvRoot 'assets/route-icons.json') + ',route-icons.json'
foreach ($rvSet in 'geoip-ru.json.gz','geosite-category-ru.json.gz','LICENSE.txt') {
    $rvResource = if ($rvSet -eq 'LICENSE.txt') { 'GeoRules-LICENSE.txt' } else { $rvSet }
    $rvArguments += '/resource:' + (Join-Path $rvRoot ('assets/geo/' + $rvSet)) + ',' + $rvResource
}
$rvWpfResources = Join-Path $OutputDirectory 'R VPN.g.resources'
$rvWriter = [Resources.ResourceWriter]::new($rvWpfResources)
try {
    foreach ($rvName in @('Comfortaa-Regular.ttf','Comfortaa-SemiBold.ttf','Comfortaa-Bold.ttf','Comfortaa-OFL.txt','Manrope-Bold.ttf','Manrope-OFL.txt')) {
        $rvStream = [IO.File]::OpenRead((Join-Path $rvRoot ('assets/fonts/' + $rvName)))
        $rvWriter.AddResource(('fonts/' + $rvName.ToLowerInvariant()), $rvStream, $true)
    }
} finally { $rvWriter.Dispose() }
$rvArguments += '/resource:' + $rvWpfResources + ',R VPN.g.resources'
$rvSourceFiles = [string[]](Get-ChildItem -LiteralPath (Join-Path $rvRoot 'src') -Filter '*.cs' | Where-Object Name -ne 'NativeEngineHost.cs' | ForEach-Object FullName)
[Array]::Sort($rvSourceFiles, [StringComparer]::Ordinal)
& $rvCompiler @rvArguments @rvSourceFiles
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
$rvHostArgs = $rvCommon + @('/target:exe',('/out:' + (Join-Path $rvCore 'RVPN.EngineHost.exe')),
    ('/reference:' + (Join-Path $rvFramework 'mscorlib.dll')), ('/reference:' + (Join-Path $rvFramework 'System.dll')),
    (Join-Path $rvRoot 'src/NativeEngineHost.cs'),(Join-Path $rvRoot 'src/CoreFiles.cs'),(Join-Path $rvRoot 'src/AssemblyInfo.cs'))
& $rvCompiler @rvHostArgs
if ($LASTEXITCODE -ne 0) { throw 'Engine host build failed.' }
foreach ($rvName in @('sing-box.exe','wintun.dll','sing-box-LICENSE.txt','wintun-LICENSE.txt')) {
    Copy-Item -LiteralPath (Join-Path $rvRoot ('vendor/' + $rvName)) -Destination $rvCore
}
foreach ($rvName in @('Comfortaa-Regular.ttf','Comfortaa-SemiBold.ttf','Comfortaa-Bold.ttf','Comfortaa-OFL.txt','BebasNeue-Regular.ttf','BebasNeue-OFL.txt')) {
    Copy-Item -LiteralPath (Join-Path $rvRoot ('assets/fonts/' + $rvName)) -Destination $rvFonts
}
foreach ($rvName in @('RVPN.Settings.json','R VPN.exe.config')) {
    Copy-Item -LiteralPath (Join-Path $rvRoot $rvName) -Destination $rvRelease
}
# Keep the established updater's exact package file list. Put complete legal
# texts in README.md rather than adding files older clients refuse to unpack.
$rvPackageReadme = [Text.StringBuilder]::new()
foreach ($rvName in @('README.md','docs/PRIVACY.md','THIRD-PARTY-NOTICES.md','LICENSE','licenses/sing-box.txt',
    'licenses/Wintun-binaries.txt','licenses/Simple-Icons-CC0.txt','licenses/sing-box-dependencies.txt','assets/fonts/Manrope-OFL.txt')) {
    [void]$rvPackageReadme.AppendLine("`n--- " + $rvName + " ---`n")
    [void]$rvPackageReadme.AppendLine([IO.File]::ReadAllText((Join-Path $rvRoot $rvName)).Replace("`r`n","`n"))
}
[IO.File]::WriteAllText((Join-Path $rvRelease 'README.md'),$rvPackageReadme.ToString().Replace("`r`n","`n"),[Text.UTF8Encoding]::new($false))
& (Join-Path $rvRoot 'installer/build-setup.ps1') -OutputDirectory $OutputDirectory -UninstallerOnly
if ($Test) {
    $rvQa = Join-Path $OutputDirectory 'qa'
    $rvProcess = Start-Process -FilePath (Join-Path $rvRelease 'R VPN.exe') -ArgumentList @('--qa',('"' + $rvQa + '"')) -WindowStyle Hidden -PassThru
    if (!$rvProcess.WaitForExit(60000)) { Stop-Process -Id $rvProcess.Id -Force; throw 'Desktop QA timed out.' }
    if ($rvProcess.ExitCode -ne 0) {
        $rvReport = Join-Path $rvQa 'desktop-checks.json'
        if (Test-Path -LiteralPath $rvReport) { Write-Output ([IO.File]::ReadAllText($rvReport)) }
        throw ('Desktop QA failed. Inspect ' + $rvReport)
    }
    Write-Output ('Desktop QA passed: ' + (Join-Path $rvQa 'desktop-checks.json'))
}
if ($Package) {
    $rvArchive = Join-Path $OutputDirectory ('R-VPN-Windows-x64-' + $rvVersion + '.zip')
    & (Join-Path $rvRoot 'scripts/New-Archive.ps1') -Directory $rvRelease -Archive $rvArchive -Prefix 'R VPN/'
    & (Join-Path $rvRoot 'installer/build-setup.ps1') -Archive $rvArchive -OutputDirectory $OutputDirectory
}
Write-Output ('Built R VPN ' + $rvVersion + ' from public-client sources into ' + $OutputDirectory)
