# Changelog

This project follows the structure of Keep a Changelog and uses Semantic Versioning for release tags.

## Unreleased

### Added

- Multi-selection, current-page filtering, stable typed sorting, selection summaries, and Explorer-style browser commands.
- Preflighted bulk file/folder downloads and permanent bulk deletion with overlap deduplication, cancellation, progress, conflict handling, and safe local path mapping.
- Public URL copying, temporary SigV4 download links, Preview and Properties details, and bounded session preview caching.
- Tray-owned application lifetime, close/minimize-to-tray behavior, per-user startup registration, and named-mutex/named-pipe single-instance activation.
- GitHub CI, tagged-release automation, dependency update configuration, repository hygiene, and release documentation.
- A per-user single-EXE Inno Setup installer with desktop/Start menu shortcuts, Windows uninstall registration, in-use file handling, and in-place upgrades.
- Consent-based automatic update checks using a versioned Cloudflare R2 installer plus an HTTPS manifest, size, and SHA-256 verification.
- Reproducible Cloudflare R2 update publishing scripts and manifest schema.

### Changed

- Release packages now include required managed dependencies and WebView2 loader files instead of enforcing an artificial one-executable layout.
- Generated build outputs are no longer tracked.

### Security

- HTML and SVG previews remain source text, XML external entities are disabled, preview downloads are bounded, and signed URLs are not logged or persisted.
