# WPF Modernization and Repository Hardening Design

**Date:** 2026-08-17

**Repository:** `Saboreq/CloudflareR2Uploader`

**Base commit:** `bc778aaa39b9dffbfa6417f59d603632df94f91c`

**Delivery branch:** `agent/modernize-wpf-hardening`

## Purpose

Cloudflare R2 Uploader will have one supported application architecture: the .NET 10 WPF client and its Core, Infrastructure.R2, and Platform.Windows libraries. The retired .NET Framework 4.8 WinForms frontend and its compatibility build will be removed. The same change set will fix the security, shutdown, persistence, verification, bulk-download, privacy, and user-interface defects identified in the manual repository review.

The result must preserve existing WPF behavior and on-disk compatibility, produce a clean SDK-style repository, pass the complete Windows build/test/package workflow, and merge into `main` as one squashed implementation commit after independent review.

## Scope Decisions

The implementation is a focused modernization rather than either an inline patch or a repository-wide rewrite.

- Remove the compatibility frontend and the temporary source-linking bridge completely.
- Keep historical data migration code that is still required to read settings and credential files written by older releases.
- Keep `System.Windows.Forms.NotifyIcon` isolated in the Windows platform adapter. This is a supported WPF tray integration, not a second frontend.
- Move retained tests to the .NET 10 test project that owns the tested layer.
- Add narrowly scoped production abstractions only where they make failure behavior deterministic and testable.
- Do not perform broad formatting, dependency upgrades, naming changes, or a repository-wide nullable-reference migration.
- Add an MIT application license, as approved by the repository owner.

## Final Repository Shape

The canonical root solution will be `CloudflareR2Uploader.sln` and will contain only:

- `src/CloudflareR2Uploader.Core`
- `src/CloudflareR2Uploader.Infrastructure.R2`
- `src/CloudflareR2Uploader.Platform.Windows`
- `src/CloudflareR2Uploader.Wpf`
- `tools/CloudflareR2Uploader.UpdateSigner`
- the four corresponding .NET 10 test projects

The following compatibility artifacts will be removed:

- the existing legacy `CloudflareR2Uploader.sln`
- `CloudflareR2Uploader.Wpf.sln` after its modern project graph is moved to the canonical solution name
- `src/CloudflareR2Uploader`
- `tests/CloudflareR2Uploader.Tests`
- `docs/WPF_MIGRATION_PLAN.md`
- `docs/WPF_REDESIGN_PROGRESS.md`

Retained tests currently linked from `tests/CloudflareR2Uploader.Tests` will be moved physically into their owning Core, Infrastructure, or Platform test directory. `UiConstructionTests.cs` and the old test assembly metadata will be deleted because they test only the removed WinForms UI. The small `TemporaryDirectory` and `CultureScope` helpers will live under `tests/Shared` and be linked explicitly by the test projects that consume them.

With no non-SDK project remaining, shared SDK properties will move from `build/Modern.props` to root `Directory.Build.props`. The test-only property file will be renamed from `build/ModernTests.props` to `build/Tests.props`. Compatibility-only properties, analyzer suppressions, comments, package notices, and contributor instructions will be removed or rewritten. Nullable annotations will remain enabled for WPF and for already annotated files; expanding nullable coverage across the migrated libraries is outside this focused change.

## Signed Update Channel

### Trust model

HTTPS and an installer hash protect transport and accidental corruption but do not authenticate a manifest when the manifest and installer share one writable origin. New clients will therefore require every update manifest to carry an RSA-PSS/SHA-256 signature made by an offline private key.

The private key will be stored only in a password-protected PFX chosen by the repository owner. It will never be generated in this session, committed, uploaded as an artifact, logged, or passed on a command line. The corresponding RSA SubjectPublicKeyInfo value will be supplied to the release build and embedded in `update-source.json` beside the manifest URL. A build that supplies one of the URL or public key without the other will fail.

### Backward-compatible manifest

The existing schema-1 fields remain unchanged so already-installed clients can read the first signed transition manifest. Two fields are added:

- `signatureAlgorithm`, fixed to `RSA-PSS-SHA256`
- `signature`, base64-encoded signature bytes

Old clients ignore these additional JSON fields. New clients reject an absent algorithm, an absent or malformed signature, an unsupported algorithm, an invalid public key, or a failed signature before treating the manifest as an update.

The signed payload is produced by one shared .NET implementation. It uses a versioned binary format containing the schema version, semantic version, installer URL, lowercase SHA-256, size, normalized UTC publication timestamp, and notes in a fixed order with length-prefixed UTF-8 strings. The signature fields themselves are excluded. Both the signer and verifier call this implementation, avoiding PowerShell/JSON canonicalization differences.

### Tooling and publishing

`tools/CloudflareR2Uploader.UpdateSigner` will be a small .NET 10 console application. It will:

- load a password-protected PFX using a password read from a named environment variable;
- construct and validate an `UpdateManifest` from a versioned installer;
- sign the canonical payload with RSA-PSS/SHA-256;
- write deterministic UTF-8 JSON without exposing key material;
- optionally export the public SubjectPublicKeyInfo value for release configuration.

`deploy/Publish-CloudflareUpdate.ps1` will invoke this tool, upload the immutable installer first and the signed manifest last, then download and validate the published manifest. Wrangler will use an exact version rather than `latest`. Release documentation will include owner-run key creation/configuration steps and a rotation procedure. Tests use ephemeral test keys only.

`UpdateService` will load a strongly typed update source rather than only a URL. It will verify the manifest signature before version comparison or download. Installer size and SHA-256 checks remain mandatory. The installer host remains HTTPS-only, and the verified installer is launched only after orderly application shutdown preparation completes.

## Orderly Upload Shutdown

`UploadQueueService.CancelAll` currently signals cancellation but does not represent completion. A new awaitable lifecycle contract will separate those concepts:

```csharp
public interface IUploadQueueLifecycle
{
    bool IsRunning { get; }
    IReadOnlyList<UploadQueueItem> GetItems();
    Task StopAsync(bool preserveParts, CancellationToken cancellationToken);
}
```

`UploadQueueService` will implement the contract. `StopAsync` will set every item's preservation choice, signal cancellation, capture the current run task under the existing synchronization lock, and await that exact task. This lets multipart uploads drain already-started parts and then persist resume state or complete the remote abort before exit continues.

`ApplicationExitCoordinator` will depend on the lifecycle interface. After the existing user choice, it will wait with a 45-second timeout. Timeout or cleanup failure will be logged, shown as an error, and return `false`; the app will remain open rather than claim that it is safe to exit. Tray exit and update installation will continue only after a successful stop. Dependency injection will bind the existing queue singleton to both its concrete service and lifecycle interface.

Synchronous disposal will never dispose the cancellation source or pause controller while the captured run task is still active. Planned exits are expected to finish through `StopAsync`; disposal remains a defensive last line for startup failures and abnormal teardown.

## Atomic Persistence

A single internal Core utility, `AtomicFileWriter`, will own replacement semantics for settings, credentials, multipart resume state, and activity-history rewrites.

For every write it will:

1. create a unique temporary file in the destination directory;
2. write through the caller-provided stream action;
3. flush managed buffers and call `FileStream.Flush(true)`;
4. use `File.Replace` when the destination exists or a same-volume `File.Move` when it does not;
5. delete only its uncommitted temporary file on failure;
6. never delete or truncate the last valid destination before replacement succeeds.

The utility will expose an internal replacement strategy seam so tests can inject a failure exactly between temporary-file completion and replacement. Regression tests will prove that the old destination remains byte-for-byte intact and that temporary files are cleaned up. Callers retain their existing logging and boolean/error contracts.

## Upload Integrity Verification

The existing `VerifyETagAfterUpload` setting will control ETag comparison rather than remaining dead state.

- A post-upload `HEAD` request and content-length comparison remain mandatory.
- When the setting is enabled, both the upload response and `HEAD` response must provide an ETag.
- ETags are normalized by trimming whitespace and one surrounding quote pair, then compared case-insensitively.
- A missing or mismatched ETag fails verification and does not mark the queue item complete.
- When the setting is disabled, size verification still runs and the returned `HEAD` ETag may be recorded without being used as an integrity gate.

This comparison applies to both single-part and completed multipart uploads and detects a same-size replacement between upload completion and verification.

## Collision-Free Bulk Downloads

Bulk download workers will receive paths from a dedicated allocator rather than independently probing the filesystem.

The allocator will use Windows-style case-insensitive identity and reserve both every final destination and its `.r2partial` companion. Original planned paths are registered before any transfer starts. Automatic rename candidates must be absent from the filesystem and unreserved as either another final or partial path. Ask-mode decisions that select rename will allocate under the same lock before opening a file.

Workers may run concurrently only after path ownership is established. A deterministic barrier-based test will reproduce the former `report.txt` / `report (1).txt` collision and prove that each remote object receives a distinct final and partial path.

## Activity Deletion and Update Text

`IActivityHistoryService.ClearAsync` will return `Task<bool>`. It will return `true` and raise `Cleared` only after the history file is absent. Recoverable deletion failure will be logged and return `false`. `ActivityViewModel` will display success only for `true`; otherwise it will preserve/reload the records and show an error.

Update-download status text will be generated by a small internal formatter used by `SettingsViewModel`. It will emit the Unicode ellipsis character `…` and culture-appropriate progress values, eliminating the committed mojibake. A direct unit test will cover both known- and unknown-length progress.

## Repository and Supply-Chain Hardening

- Add the standard MIT license with copyright year 2026 and owner name `Saboreq`.
- Add `.github/dependabot.yml` for weekly NuGet and GitHub Actions updates with grouped, bounded pull requests.
- Pin GitHub Actions to full commit SHAs with human-readable version comments.
- Pin Inno Setup and Wrangler to exact reviewed versions.
- Keep workflow permissions at least privilege and continue requiring the complete build/test/package script.
- Update `README.md`, `CONTRIBUTING.md`, `SECURITY.md`, `THIRD-PARTY-NOTICES.md`, release documentation, and deploy documentation for the WPF-only architecture and signed updater.
- After merge, enable required CI checks on `main`, enable Dependabot alerts, and require reviewed pull requests if the available GitHub administration interface supports those settings. If the connector cannot mutate a setting, the exact remaining owner action will be reported rather than implied complete.

## Compatibility and Release Transition

User data paths, settings schema migration, DPAPI credential migration, multipart state identifiers, and installer application identity remain unchanged. Removing the legacy source does not remove readers for old files.

The first release from this branch must be built with both the update manifest URL and the trusted public key. Its signed schema-1 manifest can still be consumed by older clients because the added signature fields are ignored by their JSON deserializer. After installing that release, all subsequent update checks require a valid signature. This is the strongest transition possible without modifying clients that are already installed.

No production signing key is required for ordinary developer builds or CI tests. Update checks are disabled when no complete update-source configuration is embedded. CI exercises signing with ephemeral keys and synthetic URLs.

## Test Strategy

Every behavior change follows red-green-refactor. A regression test is committed and observed failing for the defect before its production implementation is added.

Required automated coverage includes:

- signed manifest accepted; unsigned, altered, malformed, and wrong-key manifests rejected;
- signer output accepted by the runtime verifier without a second canonicalization implementation;
- release configuration rejects an update URL without a public key and vice versa;
- exit coordinator waits for lifecycle completion and refuses exit after timeout/failure;
- queue stop waits for the captured run task and does not dispose live synchronization resources;
- atomic replacement preserves the prior settings, credential, resume-state, and activity bytes when replacement fails;
- ETag setting enabled/disabled, quoted values, missing values, mismatch, and same-size replacement behavior;
- parallel bulk rename collision with distinct final and partial paths;
- activity-clear success and recoverable failure UI behavior;
- update progress text uses `…` and has no mojibake;
- the canonical .NET 10 solution restores, builds, tests, publishes, and produces exactly one validated installer.

The execution environment does not currently contain `dotnet`, MSBuild, or a Windows desktop runtime. The feature branch will therefore use the repository's Windows GitHub Actions job for observable failing and passing test cycles. Temporary development history remains on the branch; the PR is squash-merged so `main` receives one cohesive implementation commit.

Before merge, a separate read-only reviewer will compare the complete branch against this specification and classify any Critical, Important, or Minor issues. All Critical and Important issues must be resolved and the full workflow rerun. After squash merge, the workflow must be checked again against the resulting `main` commit.

## Acceptance Criteria

The work is complete only when all of the following are true:

- no .NET Framework target, legacy WinForms frontend, compatibility solution, or legacy-only test remains;
- the root solution contains only supported .NET 10 projects and the update-signing tool;
- every confirmed review finding has a regression test and a verified fix;
- no production private key or generated release artifact is tracked;
- all documentation describes the actual WPF-only build and signed update process;
- the Windows restore/build/test/publish/package workflow succeeds on the final feature commit;
- independent review has no unresolved Critical or Important issue;
- the PR is squash-merged into `main` and the merged commit's workflow succeeds;
- repository settings that cannot be changed through the available GitHub interface are listed explicitly for the owner.

## Explicit Non-Goals

- replacing `NotifyIcon` with custom shell P/Invoke;
- redesigning the WPF user interface;
- changing user-data locations or removing backward-compatible data readers;
- upgrading unrelated NuGet dependencies;
- converting every migrated source file to nullable-reference annotations;
- code-signing the Windows executable with a commercial Authenticode certificate. Manifest signing is implemented independently and Authenticode remains a recommended future defense in depth.
