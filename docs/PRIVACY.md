# R VPN client privacy policy

Prepared 2026-11-07 for Windows client 0.1.19. Project maintainer: R PROJECT,
[RecconD](https://github.com/RecconD). Privacy contact: reccondesign@gmail.com.
This policy describes the client and does not claim an independent audit of
hosting-provider or server log retention.

## Data needed to operate the client

When you activate access, the configured activation service receives the code
you enter and a randomly generated local device ID. This is not a hardware
fingerprint. Profile refresh and connection requests send the access token and,
when selecting an exit, the requested node ID. HTTPS requests identify the
client type in the User-Agent. Network servers can observe the source IP,
request time and normal connection metadata.

While access is being used, profile refreshes validate subscription status.
The VPN service handles your tunneled traffic and reports the account's
traffic totals and subscription status. This client policy does not promise
that an independently operated server retains no logs or that server records
are deleted on a particular schedule. The R VPN operator must publish and
enforce its server policy separately.

## Optional requests

Automatic update checks are disabled by default. If enabled, the client
requests a release manifest after startup and every two hours. Manual
"Check for updates" requests are made when you press that button. Downloading
and installing a release requires your confirmation. Update servers receive
normal HTTPS connection metadata. The RSA update verification public key is
included in source. Its private key is not part of this repository.

Optional connection-result diagnostics are disabled by default. If enabled,
the activation service receives the attempted VPN port, success/failure and
node ID, authenticated with the existing access token. The diagnostic does not
send browser history or the contents of traffic.

The installer provides separate options for both optional functions. Their
per-user choices are saved in %LOCALAPPDATA%\R VPN\privacy.json. Set
automaticUpdateChecks and connectionDiagnostics to false there to disable
them later. Portable users can set the same fields in RVPN.Settings.json.

## DNS, probes and routing

Server selection tests make HTTPS requests to the candidate node's probe URL.
Tunnel validation requests the configured probe URL, by default Google's
https://www.gstatic.com/generate_204. The default secure DNS resolver is
Cloudflare DNS over HTTPS. Direct-routing exceptions can use local/system
DNS and the normal internet connection. The respective services receive the
queries or connection metadata necessary for these requests. Their policies
are at https://policies.google.com/privacy and
https://developers.cloudflare.com/1.1.1.1/privacy/public-dns-resolver/.

## Local storage and system changes

Activation state and the VPN profile are stored in the current Windows user
profile and protected with Windows DPAPI. Routing preferences and optional
privacy settings remain on your computer. Network-core runtime files are
created locally as needed. The VPN requires a Wintun adapter and temporarily
changes network routes and DNS. Windows may request elevation. Driver and
network-engine processes run locally. No advertising SDK is included.

Uninstallation offers removal of the local activation and settings. Removing
local files does not itself delete account or traffic records on the service.
Contact the service operator for those requests. The uninstall action does not
grant a new subscription or deactivate someone else's credentials.

## Contact

Contact reccondesign@gmail.com for privacy questions and account data requests.
Subscription expiry does not by itself erase service records. The service's
account deletion operation clears device binding, activation material and
traffic details and retains minimal revocation identifiers to prevent key reuse.
There is no blanket retention period promised for web-server/provider logs or
backups in this client policy. Do not treat it as a verified no-logs claim.
