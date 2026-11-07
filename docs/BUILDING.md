# Build and verify Windows releases

Use Windows 10/11 or a GitHub-hosted windows-2022 runner, .NET Framework 4.8 and
**Windows PowerShell 5.1**. No Visual Studio installation or private server file
is needed. Network access is needed for the first pinned dependency restore.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test -Package
```

Output is `build/R VPN/R VPN.exe`, `build/R VPN/core/RVPN.EngineHost.exe`,
`build/R VPN Uninstall.exe`, the portable ZIP and the setup EXE. The build
refuses a pre-existing application output folder. Use `-OutputDirectory` to
build into a separate folder. `-Offline` uses already downloaded archives but
still verifies their checksums and extracts fresh toolchain directories.

Desktop QA checks the declared 1120x700 window size before showing it, then
checks its actual size against Windows' maximum tracking dimensions. Hosted
runners have a smaller virtual desktop and may clamp the native window; local
visual review at the full 1120x700 size remains part of release verification.

Roslyn, .NET reference assemblies, sing-box and Wintun are pinned by version
and SHA-256. The runtime verifies the exact upstream executable and DLL hashes
again before starting the engine. Do not modify or sign these upstream files.

Owned .NET binaries use deterministic compiler output, a fixed source path map,
fixed file ordering and one shared AssemblyInfo version. ZIP entries use fixed
timestamps and ordinal ordering. Rebuilding the same source with the pinned
toolchain on the same Windows PowerShell/.NET platform is checked by comparing
hashes. A timestamped Authenticode signature is time-dependent and will not be
byte-identical between independent signing requests.

Version 0.1.19 is a new publication candidate with licensing/privacy additions
and reproducible-build tooling. It is not claimed to be byte-identical to the
previously distributed unsigned 0.1.18 installer. The network engine and Wintun
are fetched as exact official upstream binaries, not rebuilt as R VPN code.

The installed/portable file list remains compatible with 0.1.18's strict updater.
Complete legal texts are appended to the package README.md. The repository's
separate legal files are kept for readable source review.

## Corresponding source

Run `scripts/Get-GoModuleSources.ps1` to fetch the module source inventory
recorded in the upstream executable. It verifies Go `h1` directory hashes, not
only ZIP transfer checksums. `scripts/New-SourceBundle.ps1` creates the own
source and corresponding upstream source bundle for every release. Never
publish a GPL binary release without its corresponding-source bundle.

## Signing order

1. Build the application, host and uninstaller from the reviewed source commit.
2. Send exactly those three owned PE files to the `client-binaries` SignPath
   configuration. Approve the request manually.
3. Require a trusted Windows Authenticode chain, approved publisher and trusted
   timestamp on all three returned binaries.
4. Put the signed application and host in the ZIP. Compile the installer with
   that ZIP and the already signed uninstaller as embedded resources.
5. Send only the setup EXE to the `windows-installer` configuration and approve
   that request manually.
6. Verify the setup signature and timestamp, extract its resources without
   executing the installer and verify the nested files again. Record hashes.

Only steps 1 and verification of unsigned candidates can run before approval.
The prepared signed workflow fails closed unless the acceptance, Wintun and
owner gates are explicitly configured. It does not publish to the website or
alter the private RSA updater key.
