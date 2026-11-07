# Code signing policy

Status as of 2026-11-07: **proposed, not active**. R VPN has not been accepted by
SignPath Foundation, no application has been submitted, and no R VPN-owned
binary has a trusted Authenticode signature from this preparation.

After acceptance the policy will identify the service as follows:
"Free code signing provided by [SignPath.io](https://about.signpath.io/),
certificate by [SignPath Foundation](https://signpath.org/)".
Until acceptance that sentence is a proposed attribution, not a claim of
sponsorship. The certificate publisher will be SignPath Foundation, while the
product metadata remains R VPN / R PROJECT.

## Team and approvals

The project owner is the maintainer for these roles. Additional reviewers can
be added as the project grows. Account/MFA setup still requires owner action.

| Role | Current status |
| --- | --- |
| Committers/authors | [RecconD](https://github.com/RecconD) |
| Reviewers | [RecconD](https://github.com/RecconD) |
| Release signing approvers | [RecconD](https://github.com/RecconD) |

Contact: reccondesign@gmail.com. SignPath account and MFA configuration are
pending, and this table does not claim that they have already been configured.

MFA is required for source-control and SignPath accounts. Review outside
contributions, especially build and workflow changes. Protect the release tag
and default branch and configure the GitHub `code-signing` environment with a
required human reviewer. Every SignPath signing request requires a human
approval, including both requests in a release.

## Signing scope

Only R VPN.exe, RVPN.EngineHost.exe, R VPN Uninstall.exe and the resulting
R-VPN-Setup-version.exe are eligible for this project's requests. They must be
built from reviewed source on GitHub-hosted Windows runners. The XML
configurations require exact product, version and company metadata.

The upstream sing-box executable, Wintun DLLs and any native Cronet payload are
not owned R VPN binaries and must not be re-signed under this project policy.
Their immutable checksums and upstream notices are retained. Bundling Wintun is
blocked for Foundation signing until SignPath accepts its system-library status
in writing or approves a compliant alternative. Do not route around this check.

## Activation after acceptance

1. Link the public repository to the SignPath GitHub trusted-build connector.
   Install the official GitHub App with access only to this repository.
2. Import `signpath/client-binaries.xml` as `client-binaries` and
   `signpath/windows-installer.xml` as `windows-installer`.
3. Have SignPath configure a release-signing policy using its production
   Foundation certificate, SHA-256 Authenticode and a trusted RFC 3161 timestamp.
   Timestamp service details belong to that service-side signing configuration.
   No invented timestamp XML attributes or local trust-store changes are used.
4. Put `SIGNPATH_API_TOKEN` only in the protected environment's Actions secrets.
   The owner creates the token after acceptance. Do not create or store it in Git.
5. Configure repository variables `SIGNPATH_ORGANIZATION_ID`,
   `SIGNPATH_PROJECT_SLUG`, `SIGNPATH_SIGNING_POLICY_SLUG` and
   `SIGNPATH_WINTUN_APPROVAL_URL` with the approved project and decision.
   Record completion of the native dependency/source review in
   `SIGNPATH_SOURCE_REVIEW_URL` and set `SOURCE_CORRESPONDENCE_APPROVED` only
   after the missing source/license questions have actually been resolved.
6. Set `PUBLICATION_APPROVED`, `SIGNPATH_ACCEPTED` and
   `WINTUN_SYSTEM_LIBRARY_APPROVED` to `true` only when those events really
   happened. The last flag specifically records SignPath's written approval,
   not a local reinterpretation of their terms.
7. Run `Sign approved Windows release` on the matching reviewed version tag.
   Downloading a result into a folder named "signed" does not establish trust.
   The workflow requires Windows validation and timestamps before exposing its
   final artifact. Failure never falls back to an unsigned release.

## Verification and release evidence

`scripts/Assert-Signatures.ps1` checks Windows `Get-AuthenticodeSignature`, the
approved publisher name, the code-signing EKU and timestamp certificate.
Signed releases also run `signtool verify /pa /all /tw /v`. The test reads setup
resources without executing installation, compares the embedded ZIP with the
portable ZIP and checks the nested application, host and uninstaller again.

Review `signature-verification.json` and `SHA256SUMS.txt` with the source commit
and SignPath request links. Only after that review should a release be uploaded
to the website. The private RSA manifest signature used by the existing updater
is a separate private deployment step after these files are final. It is not
Authenticode, and that key stays outside this public repository.

A valid signature proves publisher/bytes integrity according to Windows trust
rules. It does not guarantee an absence of SmartScreen reputation warnings on
a new executable. Self-signed certificates and imported test roots are not an
acceptable substitute for the requested public trust.

References checked 2026-11-07:
- https://signpath.org/terms.html
- https://docs.signpath.io/trusted-build-systems/github
- https://docs.signpath.io/artifact-configuration/syntax
- https://docs.signpath.io/artifact-configuration/reference
- https://learn.microsoft.com/windows/apps/package-and-deploy/smartscreen-reputation

Privacy policy: [PRIVACY](PRIVACY.md). This policy must also be linked under
"Code signing policy" on https://r-project.lol/ and the download/release pages
after the owner approves the public repository and the operator details.
