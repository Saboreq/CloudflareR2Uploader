# Security policy

Report vulnerabilities through GitHub private vulnerability reporting on this repository when that feature is enabled. If it is unavailable, open a minimal public issue asking a maintainer to establish a private channel. Do not include exploit details, credentials, signed URLs, bucket names, object data, signing material, or manifest drafts in a public issue.

Sensitive data includes R2 access keys and secrets, authorization headers, presigned URL query strings, private object content, local preview-cache files, resumable upload state, update-signing PFX/private keys, and signing passwords. R2 credentials are stored separately from settings and protected for the current Windows user with DPAPI. Presigned links are bearer tokens and should be revoked by expiry or credential/key rotation if exposed.

The update channel authenticates schema-1 manifests with RSA-PSS/SHA-256 before version comparison or installer download. Installer size and SHA-256 verification remain mandatory after signature validation. A production private key must remain outside the repository and release artifacts; only its public SubjectPublicKeyInfo is configured for release builds. See [deploy/README.md](deploy/README.md) for the publisher trust and recovery procedure.

Security fixes are supported on the latest published release. Older releases may be asked to upgrade before receiving a fix. The project does not publish an email address that can safely receive vulnerability reports.
