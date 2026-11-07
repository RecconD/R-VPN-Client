# SignPath Foundation eligibility review

Reviewed 2026-11-07 against https://signpath.org/terms.html. The owner approved
public publication and GPL-3.0-or-later on 2026-11-07. This review is not
acceptance by SignPath Foundation.

| Requirement | Evidence/current decision |
| --- | --- |
| Maintained, already released product and described downloads | Existing Windows 0.1.18 at https://r-project.lol/. Candidate 0.1.19 must receive its own reviewed release and source record. |
| OSI license for own code | No previous LICENSE file was present in the private Windows project. The owner approved GPL-3.0-or-later for client code, artwork and build scripts on 2026-11-07. No commercial dual license is used. |
| Dependencies | Exact sing-box binary matches official 1.14.4 and is GPL-3.0-or-later with upstream naming notice. All 135 compiled Go module archives passed their recorded Go h1 sums. Primary license notices were inspected/classified and their text is preserved. This is not a legal certification. |
| No proprietary bundled component except accepted system libraries | **Unresolved.** Official Wintun 0.14.1 signed prebuilt DLLs use a separate binary license with restrictions, despite GPL-2.0 source. The direct DLL and embedded copies in sing-tun must be disclosed to SignPath and accepted as system libraries or removed through an approved functional alternative. No claim that the DLL is OSI-licensed is made. |
| Complete source provenance | Own binaries build from this repository with pinned compiler and references. Official sing-box is restored by SHA-256. Module source archives include prebuilt Wintun DLLs and a libcronet.dll payload. The build metadata alone does not establish native DLL source correspondence. Written upstream/source references and SignPath review are needed before claiming a fully source-built engine. |
| Project reputation and repository control | Public repository https://github.com/RecconD/R-VPN-Client is controlled by the maintainer. A new repository alone does not establish reputation. A public release history, source reviews and real usage evidence are still needed. Acceptance is discretionary even if technical checks pass. |
| Own source/binaries only for signing | XML scopes include four owned binaries only. Upstream engine/drivers are never re-signed. The ZIP and EXE installer are assembled after inner-file signing. |
| User privacy and system changes | Privacy text is embedded in setup, optional update checks and result diagnostics default off with installer choices. Network configuration changes are disclosed. The owner supplied reccondesign@gmail.com as contact. Server/provider retention is not represented as independently audited. |
| Uninstallation | Existing branded uninstaller is built from source and offers local profile cleanup. |
| Code signing policy and roles | docs/CODE_SIGNING.md assigns maintainer/reviewer/approver roles to RecconD. The homepage links signing and privacy policies. The owner submitted the application on 2026-11-07 and received an acknowledgement. Approval, SignPath account and MFA configuration remain pending. |
| Verifiable automated builds and manual approval | Public GitHub-hosted builds and logs are available in Actions. Check the result for the exact release commit before distribution. Pinned actions, two manual SignPath approvals and fail-closed signature checks are prepared. No trusted signature is claimed yet. |

## Exact Wintun question for the application

Will SignPath accept the original, unmodified, vendor-signed Wintun 0.14.1
DLLs under the System Libraries exception for this Windows TUN client?
They are external in the R VPN package and also present in the upstream
sing-tun dependency. The executable hashes, binary license and upstream
source/license distinction will be attached. We will not label these DLLs as
GPL-licensed or re-sign them with the Foundation certificate.

If SignPath declines that interpretation, signing remains disabled. Changing
the engine/driver or separating a dependency for policy reasons requires an
explicit compliant design reviewed by SignPath, not an undisclosed download
step that evades their no-proprietary-components rule.

## Free alternative if Foundation signing is unavailable

Microsoft documents free Store signing for an **MSIX submitted to the Store**.
It is a different distribution path and requires Store eligibility, account
verification and technical adaptation of this elevated TUN client. It does not
give a free Authenticode signature to the current standalone setup EXE. The
Store MSI/EXE submission path requires the publisher's own trusted signatures.
Free individual onboarding also has account-purpose/market conditions, which
must be checked for the owner's actual business and country.

Sources:
- https://www.wintun.net/ (source GPL-2.0 vs signed prebuilt binary terms)
- https://signpath.org/terms.html (system-library exception and reputation)
- https://learn.microsoft.com/windows/apps/package-and-deploy/code-signing-options
- https://learn.microsoft.com/windows/apps/publish/partner-center/open-a-developer-account

No guaranteed free general-purpose CA certificate for this unpublished,
closed-source EXE distribution has been identified. Do not create a self-signed
certificate and describe it as a trusted publisher for arbitrary Windows PCs.
