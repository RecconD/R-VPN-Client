# R VPN for Windows

Public source candidate, version 0.1.19. SignPath Foundation declined the
application on 2026-11-07 because the project has not yet established sufficient
public recognition. Its binaries remain unsigned and signing is disabled.

R VPN is a Windows 10/11 x64 desktop VPN client built with C# and WPF on .NET
Framework 4.8. It connects to an R VPN service with a user-supplied activation
code. The client supports VLESS over TLS/WebSocket and REALITY through the
unmodified upstream sing-box network engine.

Features include server connection checks and selection, traffic counters,
connection time, a profile and update screen, per-service and domain routing,
and an ordered rule editor. A VPN connection changes Windows routes and DNS
and may request administrator permission. Disconnecting stops the tunnel.

The repository contains the Windows client and installer only. Running a VPN
service requires a compatible independently operated activation API and a VPN
server. No free subscription, server access or activation keys are included.
The default public API addresses and the update **public** verification key are
intentionally part of the client. They are not server credentials.

Project website: https://r-project.lol/

## Build

On Windows with .NET Framework 4.8 and Windows PowerShell 5.1:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test -Package
```

Pinned dependencies are downloaded and checked against SHA-256 values in
`dependencies.lock.json`. A new build never uses binaries from the private
parent project. See [BUILDING](docs/BUILDING.md) and
[dependency notices](THIRD-PARTY-NOTICES.md).

## Licensing and publication status

The license for R PROJECT-owned client code, build scripts and artwork is
GPL-3.0-or-later, with the full license in [LICENSE](LICENSE). The owner approved
public publication under this license on 2026-11-07.
Third-party files retain their original licenses and notices.

Repository: https://github.com/RecconD/R-VPN-Client
Project contact: reccondesign@gmail.com

## Privacy

Read the [privacy policy](docs/PRIVACY.md), which is also included in the installer.
Automatic update checks and optional connection-result diagnostics are disabled
by default. The installer lets the user enable them. Activation, VPN connection
and manual update requests necessarily contact the selected services.

## Code signing policy

See [Code signing policy](docs/CODE_SIGNING.md). SignPath integration is prepared
but disabled until the project has been accepted, team identities and policy
have been configured, and the Wintun license question has been resolved in
writing. A proposed policy is not evidence of a certificate or a signed release.

## Security

See [SECURITY](SECURITY.md). Never put activation keys, bearer tokens, private
keys, server passwords or personal VPN profiles into an issue or pull request.
