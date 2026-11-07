param([Parameter(Mandatory=$true)][string]$Executable, [Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference = 'Stop'
if (!('RvpnGoBuildInfo' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Text;
public static class RvpnGoBuildInfo {
    static string ReadString(byte[] bytes, ref int at) {
        ulong n = 0; int shift = 0; byte b;
        do { b = bytes[at++]; n |= (ulong)(b & 127) << shift; shift += 7; if (shift > 63) throw new Exception("Bad varint"); } while (b > 127);
        if (n > (ulong)(bytes.Length - at)) throw new Exception("Bad string length");
        string value = Encoding.UTF8.GetString(bytes, at, (int)n); at += (int)n; return value;
    }
    public static string[] Read(byte[] bytes) {
        byte[] magic = {255,32,71,111,32,98,117,105,108,100,105,110,102,58};
        for (int i = 0; i < bytes.Length - 32; i++) {
            if (bytes[i] != 255) continue;
            int j; for(j=1;j<magic.Length && bytes[i+j]==magic[j];j++) {}
            if (j != magic.Length) continue;
            if ((bytes[i+15] & 2) == 0) throw new Exception("Pointer format is not supported");
            int at = i + 32;
            string version = ReadString(bytes, ref at), modules = ReadString(bytes, ref at);
            if (modules.Length < 32) throw new Exception("Missing modules");
            return new [] {version, modules.Substring(16, modules.Length-32)};
        }
        throw new Exception("Go build info was not found");
    }
}
'@
}
$rvData = [RvpnGoBuildInfo]::Read([IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $Executable).Path))
$rvModules = [Collections.Generic.List[object]]::new()
foreach ($rvLine in $rvData[1].Split("`n")) {
    $rvFields = $rvLine.Split("`t")
    if ($rvFields[0] -eq 'dep') {
        $rvModules.Add([pscustomobject]@{path=$rvFields[1];version=$rvFields[2];sum=$rvFields[3];originalPath=$null;originalVersion=$null})
    } elseif ($rvFields[0] -eq '=>') {
        $rvLast = $rvModules[$rvModules.Count-1]
        $rvLast.originalPath=$rvLast.path; $rvLast.originalVersion=$rvLast.version
        $rvLast.path=$rvFields[1]; $rvLast.version=$rvFields[2]; $rvLast.sum=$rvFields[3]
    }
}
if (@($rvModules | Where-Object { $_.sum -notmatch '^h1:.{43}=$' -or $_.version -notmatch '^v' }).Count) { throw 'Unpinned module found.' }
$rvJson = ($rvModules | ConvertTo-Json -Depth 4).Replace("`r`n","`n")
[IO.File]::WriteAllText([IO.Path]::GetFullPath($Output), $rvJson + "`n", [Text.UTF8Encoding]::new($false))
Write-Output ('Go ' + $rvData[0] + ': ' + $rvModules.Count + ' pinned modules, including replacements.')
