# SignPath Foundation application — declined for now

The owner submitted the form at https://signpath.org/apply.html on 2026-11-07.
The page displayed "Form submitted" and the owner received the Foundation's
"Open Source Project Submitted" acknowledgement email. This confirms receipt,
not acceptance. The owner performs account login, identity confirmation and
MFA setup. No signing account approval or certificate is claimed.

On 2026-11-07 the owner supplied the Foundation's decision email. The
application was declined because the project does not yet show sufficient
public trust, visibility and sustained engagement. The Foundation invited a
later application after broader recognition. It did not set a guaranteed
number of stars, users or months that would ensure acceptance. Signing stays
disabled. No reply or second application has been sent by this tooling.

Project: R VPN for Windows
Website/download page: https://r-project.lol/
Repository: https://github.com/RecconD/R-VPN-Client
Public workflow URL: https://github.com/RecconD/R-VPN-Client/actions
Build status: see the individual public workflow runs; no signed release
License: GPL-3.0-or-later for own client code, approved by owner on 2026-11-07
Contact: reccondesign@gmail.com (supplied by owner, email verification pending)
Authors/reviewers/approvers: https://github.com/RecconD (MFA setup to be confirmed)

## Proposed application description

R VPN is a C#/WPF Windows 10/11 x64 VPN client with activation through an
operator-controlled HTTPS API. It uses an unmodified upstream sing-box
network engine for VLESS TLS/WebSocket and REALITY, and Wintun for the Windows
TUN adapter. It includes routing preferences, ordered rules, connection checks,
traffic counters, profile controls and user-confirmed updates. The backend and
VPN service are separate private services. Access to those services may be
sold, while the proposed client code license has no commercial dual license.

We request signing only for our client, local host process, uninstaller and
installer, all compiled by the public GitHub-hosted workflow from reviewed
source. We will not re-sign upstream sing-box, Wintun or Cronet binaries.
Inner files are signed first and embedded in the installer before its own
signing request. Each request requires a human release approval. Optional
connection reports and automatic update checks have user controls and default
off. Setup discloses VPN adapter, routing and DNS changes and provides removal.

We are disclosing a licensing question before asking for production signing.
Wintun's signed prebuilt DLL has its own binary license rather than the GPL-2.0
source license. Its exact vendor binaries, including upstream embedded copies,
are unchanged. Please decide whether these qualify as System Libraries under
your policy. We will not enable the workflow without a written decision.
The official network-engine source snapshot and all recorded Go module sources
are archived. Some upstream module archives contain native prebuilt DLLs whose
source correspondence is separately identified in the eligibility review.

R VPN is a new public-source candidate. We do not claim pre-existing public
repository reputation or Foundation acceptance. Please identify any additional
reputation, source provenance or privacy requirements needed for consideration.

## Attach/refer to these items after publication

- Public repository and immutable source commit/tag
- Passing GitHub workflow and unsigned artifact links
- Build instructions and own-binary reproducibility evidence
- LICENSE, THIRD-PARTY-NOTICES.md and dependency lock/inventory
- Wintun binary license and exact vendor hashes
- Native payload/source provenance disclosure
- Code signing policy, real team role links and MFA confirmation
- Approved privacy policy with service operator contact and retention details
- Existing download history and other truthful project reputation evidence

This file records the application scope and current status, not an approval.
It contains no API token, user activation code or server credential.
