# Security policy

Do not publish credentials or private runtime state. Keep activation codes,
bearer tokens, VLESS profiles, RSA private keys, PFX files, server passwords and
database exports out of Git. The public update verification key and public API
endpoints are intentionally public.

Before publication the owner must enable GitHub private vulnerability reporting
or specify a private security contact on the project website. Until then, do
not open a public issue containing a vulnerability exploit or a secret.

Release workflows run on GitHub-hosted Windows runners. They do not run with
signing secrets on pull requests. Actions and downloaded dependencies are
pinned. SignPath policy requires MFA, review of external contributions and
manual approval of every signing request. Neither an unapproved workflow nor
an unsigned result is a signed release.
