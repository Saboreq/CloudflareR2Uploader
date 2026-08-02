# Security policy

Report vulnerabilities through GitHub private vulnerability reporting on this repository when that feature is enabled. If it is unavailable, open a minimal public issue asking a maintainer to establish a private channel; do not include exploit details, credentials, signed URLs, bucket names, or object data in the issue.

Sensitive data includes R2 access keys and secrets, authorization headers, presigned URL query strings, private object content, local preview-cache files, and resumable upload state. Credentials are stored separately from settings and protected for the current Windows user with DPAPI. Presigned links are bearer tokens and should be revoked by expiry or credential/key rotation if exposed.

Security fixes are supported on the latest published release. Older builds may be asked to upgrade before receiving a fix. The project does not publish an email address that can safely receive vulnerability reports.
