# Third-party components

The client does not link third-party managed libraries. Windows and .NET
Framework are supplied by the operating system and are not redistributed.

| Component | Version/source | License and packaging |
| --- | --- | --- |
| sing-box | 1.14.4, commit c005f35300ce76427dc285481ec5695f5b179cd2 | GPL-3.0-or-later with upstream name/association notice. Unmodified official executable. Full license and notices are included in the binary-package README.md. Its transitive Go module inventory is recorded in docs/sing-box-modules.json. |
| Wintun prebuilt amd64 DLL | 0.14.1 | **LicenseRef-Wintun-Prebuilt-Binaries**, not the GPL-2.0 license of the source tree. Full terms in licenses/Wintun-binaries.txt. SignPath eligibility is unresolved. Do not re-sign this upstream driver. |
| Comfortaa | bundled regular, semibold, bold | SIL Open Font License 1.1, with reserved font name Comfortaa. Full notice in assets/fonts/Comfortaa-OFL.txt and binary fonts folder. |
| Manrope | bundled bold font | SIL Open Font License 1.1. Notice in assets/fonts/Manrope-OFL.txt, embedded with the font in the application. |
| Bebas Neue | bundled regular font | SIL Open Font License 1.1. Full notice in assets/fonts/BebasNeue-OFL.txt and binary fonts folder. |
| MetaCubeX meta-rules-dat | commit 7da04853058c4a8858f339b266353fc905ef4eb4, geoip-ru and geosite-category-ru JSON | GPL-3.0, notice in assets/geo/LICENSE.txt, embedded as GeoRules-LICENSE.txt. Original upstream snapshot and source links are in docs/DEPENDENCIES.md. |
| Service brand icon paths | Simple Icons geometry, converted to WPF path syntax | CC0-1.0, licenses/Simple-Icons-CC0.txt. Brands remain their respective owners' trademarks. Provenance is described in docs/DEPENDENCIES.md. |

Build-only tools are not shipped inside the installer. Roslyn
Microsoft.Net.Compilers.Toolset 4.14.0 uses MIT. Microsoft .NET Framework reference
assemblies 1.0.3 are build-only reference material under their NuGet package
license. Gitleaks 8.30.1 uses MIT. GitHub Actions and the SignPath action execute
in CI and retain their own upstream notices.

Binary-package README.md contains these notices, the full GPL, privacy policy,
all extracted module license texts and Manrope's OFL notice. Existing upstream
license files and font notices are also retained at their original paths. This
preserves the exact file list expected by the previously installed updater.

For GPL components, retain this notice, their full licenses, source URLs and a
corresponding-source bundle alongside every public binary release. The source
bundle must include the exact sing-box source snapshot and module archives
whose `h1` sums have been checked against the executable's Go build metadata.
The module audit records uncertainty instead of silently assuming a license.

No upstream executable or driver may be signed with R VPN's SignPath policy.
