# Task 1 report — Make the .NET 10 WPF solution canonical

Status: DONE_WITH_CONCERNS
Commit: `20a2165d507b4277036dbf6afaf053946004b15c` (`Retire the legacy WinForms architecture`)

## Implementation

- Replaced the legacy root solution with the former modern solution under `CloudflareR2Uploader.sln`; removed `CloudflareR2Uploader.Wpf.sln`.
- Deleted the legacy WinForms application under `src/CloudflareR2Uploader`, its .NET Framework test project, `UiConstructionTests.cs`, and its old assembly metadata.
- Moved all retained legacy tests into their physical Core, Infrastructure, and Platform owner projects as specified. Moved `TestSupport.cs` to `tests/Shared/TestSupport.cs`; each consuming project links only that shared helper and relies on SDK default compile discovery for its own test files.
- Converted shared SDK defaults to root `Directory.Build.props`, renamed `build/ModernTests.props` to `build/Tests.props`, and updated test-project imports. Removed `SharedSourceWithLegacy`, linked-test compatibility comments, and the compatibility-only analyzer suppression list.
- Removed `UseWindowsForms` from Platform tests; it remains only in Platform.Windows and WPF.
- Updated `build/Build-Release.ps1` to use `CloudflareR2Uploader.sln` and removed obsolete WPF migration-progress documents.
- Made `PathUtilityTests.GetRelativePath_UsesForwardSlashes` platform-safe by constructing equivalent rooted base/child paths with `Path.Combine` and the host directory separator. It still asserts the same public behavior—forward-slash relative output—and production code was not changed. On Windows this exercises the same Windows path semantics; on Linux it avoids passing Windows literals to Linux path handling.

## Files changed

- Added `Directory.Build.props`; removed `build/Modern.props`.
- Renamed `build/ModernTests.props` to `build/Tests.props`.
- Replaced the root solution, deleted the legacy WinForms source/test tree, and moved 25 retained test files plus `tests/Shared/TestSupport.cs`.
- Updated all modern `src/*/*.csproj` and `tests/*/*.csproj`, `build/Build-Release.ps1`, and the platform-safe `PathUtilityTests.cs` assertion.
- Deleted `docs/WPF_MIGRATION_PLAN.md` and `docs/WPF_REDESIGN_PROGRESS.md`.

## Required structural RED/GREEN evidence

Structural RED before edits:

```bash
rg -n '<TargetFrameworkVersion>|net48|SharedSourceWithLegacy|CloudflareR2Uploader\.Wpf\.sln|Migrated[\\/]' \
  --glob '*.csproj' --glob '*.sln' --glob '*.props' --glob '*.ps1'
```

The command returned legacy `net48` project entries, `SharedSourceWithLegacy` project and props entries, `Migrated` linked test entries, and the old solution path.

Structural GREEN after edits:

```bash
test ! -e src/CloudflareR2Uploader
test ! -e tests/CloudflareR2Uploader.Tests
test ! -e CloudflareR2Uploader.Wpf.sln
test -f CloudflareR2Uploader.sln
! rg -n '<TargetFrameworkVersion>|net48|SharedSourceWithLegacy|CloudflareR2Uploader\.Wpf\.sln|Migrated[\\/]' \
  --glob '*.csproj' --glob '*.sln' --glob '*.props' --glob '*.ps1'
```

All five checks exited `0`.

## Verification

All .NET commands used SDK `10.0.400` from `/tmp/cloudflare-r2-uploader-dotnet`, `DOTNET_CLI_HOME=/tmp/cloudflare-r2-uploader-cli-home`, `NUGET_PACKAGES=/tmp/cloudflare-r2-uploader-nuget`, telemetry/first-time/workload notifications disabled, and `NUGET_CERT_REVOCATION_MODE=offline`.

```bash
dotnet restore CloudflareR2Uploader.sln -p:EnableWindowsTargeting=true --disable-parallel
dotnet build CloudflareR2Uploader.sln -c Release --no-restore -p:EnableWindowsTargeting=true -m:1
dotnet test tests/CloudflareR2Uploader.Core.Tests/CloudflareR2Uploader.Core.Tests.csproj -c Release --no-build
dotnet test tests/CloudflareR2Uploader.Infrastructure.Tests/CloudflareR2Uploader.Infrastructure.Tests.csproj -c Release --no-build
```

- A direct restore completed successfully.
- Required normal build completed successfully with `0 Warning(s), 0 Error(s)` from the up-to-date graph.
- Core tests: `77/77` passed.
- Infrastructure tests: `24/24` passed.
- The supplied baseline evidence confirms Windows GitHub Actions at `bc778aaa39b9dffbfa6417f59d603632df94f91c` was successful for Windows-only tests and packaging; this Linux task did not alter that workflow or packaging behavior.

## Self-review

- Confirmed no legacy project directory, legacy test directory, old solution, old framework target, compatibility property, or linked `Migrated` items remains.
- Confirmed all expected retained test files reside in their required owning modern project and only `TestSupport.cs` is linked from `tests/Shared` where used.
- Confirmed only Platform.Windows and WPF declare `UseWindowsForms`.
- Ran `git diff --cached --check`; it completed without whitespace errors.

## Concerns

The brief explicitly requires removal of the legacy-only `NoWarn` list. A forced full rebuild (`dotnet build ... -t:Rebuild`) then reports `171` pre-existing analyzer warnings in these exact counts: `CA1068` 1, `CA1507` 50, `CA1510` 32, `CA1512` 6, `CA1806` 1, `CA1822` 12, `CA1825` 3, `CA1835` 5, `CA1840` 1, `CA1844` 1, `CA1845` 6, `CA1847` 1, `CA1850` 4, `CA1859` 7, `CA1861` 9, `CA1864` 1, `CA1865` 2, `CA1866` 2, `CA1872` 1, `CA2016` 3, `CA2249` 18, `CA2263` 1, and `SYSLIB0014` 4. Fixing these would be broad source/API modernization beyond Task 1; reintroducing the list would violate the task’s explicit removal requirement.

The same forced rebuild also created a temporary WPF `*_wpftmp.csproj` and failed in the .NET 10 Linux WPF toolchain with `RG1000: An item with the same key has already been added. Key: app.baml`. The required normal build succeeds after removing that generated untracked file. This transient forced-rebuild failure is documented; no source change was made to mask it.

## Fix Round 1

- Rewrote the stale Infrastructure.R2 package-version comment so it describes the current WPF-only application rather than the retired WinForms build and two front ends.
- RED: after `dotnet clean CloudflareR2Uploader.sln -c Release -p:EnableWindowsTargeting=true -m:1` and restore with `--disable-parallel`, `dotnet build CloudflareR2Uploader.sln -c Release --no-restore -p:EnableWindowsTargeting=true -m:1 -t:Rebuild -warnaserror` failed on the same 171 analyzer diagnostics documented above (23 categories).
- Automatic remediation was attempted once for the solution and once per Core, Infrastructure.R2, Platform.Windows, and WPF project with `dotnet format analyzers <target> --no-restore --severity warn`. Each invocation failed before modifying source: Roslyn's build host exited 137 because `NamedPipeClientStream` could not create/connect its Unix socket (`SocketException (13): Permission denied`) in this sandbox.
- No analyzer suppressions, disabled analyzers, or hidden-output settings were introduced. The complete manual modernization remains pending; therefore this round has no follow-up commit or GREEN gate.

## Fix Round 1 — Manual completion

Status: DONE

### Analyzer categories fixed

Manually eliminated all 171 diagnostics from `/tmp/task1-analyzer-red.log`, then fixed two cascading suggestions exposed by the next warnings-as-errors build. The original checklist was:

- Argument validation and parameter names: `CA1507` 50, `CA1510` 32, `CA1512` 6.
- Async and stream APIs: `CA1068` 1, `CA1835` 5, `CA1844` 1, `CA2016` 3.
- Allocation and concrete-type improvements: `CA1825` 3, `CA1859` 7, `CA1861` 9, `CA1864` 1, `CA1865` 2, `CA1866` 2, `CA1872` 1, `CA2263` 1.
- String/span idioms: `CA1845` 6, `CA1847` 1, `CA2249` 18.
- Cryptography and runtime APIs: `CA1840` 1, `CA1850` 4.
- Stateless members and ignored results: `CA1806` 1, `CA1822` 12.
- Obsolete transport configuration: `SYSLIB0014` 4.

The fixes use `ThrowIfNull`/`ArgumentOutOfRangeException` helpers and `nameof`, span-based concatenation, `Contains`/char overloads, static `SHA256.HashData`, memory-based stream methods, cancellation-token forwarding, `Array.Empty`, `TryAdd`, reusable constant arrays, concrete private parameter types, and static private helpers. Public instance APIs remain instance APIs; where the analyzer identified a stateless public member, its implementation now uses meaningful per-service state or a compatibility delegate rather than breaking callers by changing the member to static.

`R2ClientFactory.ConfigureTransportSecurity` remains available for source/binary compatibility but no longer touches obsolete `ServicePointManager` state. The canonical .NET 10 AWS transport is `HttpClient`-based, so `ServicePointManager` settings do not affect it; TLS is negotiated from current operating-system defaults.

### Behavior-test RED/GREEN evidence

No intended observable application behavior changed, so no new behavior regression test was required. These were API/idiom and private implementation refactors guarded by the existing suites plus the analyzer-as-error gate. RED remained the clean rebuild captured in `/tmp/task1-analyzer-red.log`: 171 errors in 23 categories. GREEN is the clean warnings-as-errors build below. Existing behavior suites remained green at 77 Core tests and 24 Infrastructure tests.

### Files changed

- Core production: `Models/R2Credentials.cs`, `Models/UploadQueueItem.cs`; `Services/ActivityCsvExporter.cs`, `ActivityHistoryService.cs`, `ActivitySanitizer.cs`, `BrowserViewService.cs`, `BulkLocalPathMapper.cs`, `LoggingService.cs`, `SettingsMigrator.cs`, `SettingsService.cs`, `UploadStateStore.cs`, `UserInteractionAbstractions.cs`; `Utilities/BoundedFileStream.cs`, `MultipartCalculator.cs`, `ObjectKeyUtility.cs`, `PathUtility.cs`, `R2BrowserPathUtility.cs`, and `R2ObjectOperationPathUtility.cs`.
- Infrastructure production: `Services/FriendlyErrorService.cs`, `R2BrowserResponseMapper.cs`, `R2BulkOperationService.cs`, `R2ClientFactory.cs`, `R2MultipartUploader.cs`, `R2ObjectBrowserService.cs`, `R2ObjectDetailsService.cs`, `R2ObjectOperationService.cs`, `R2PresignedUrlService.cs`, `R2PreviewService.cs`, `R2SinglePartUploader.cs`, `R2UploadService.cs`, `RetryService.cs`, and `UploadQueueService.cs`, plus the already-intended stale-comment correction in `CloudflareR2Uploader.Infrastructure.R2.csproj`.
- Platform production: `Services/CredentialProtectionService.cs`, `SingleInstanceCoordinator.cs`, `StartupRegistrationService.cs`, and `UpdateService.cs`.
- Tests: Core `ActivityCsvExporterTests.cs`, `BrowserViewServiceTests.cs`, `BulkLocalPathMapperTests.cs`, `PathUtilityTests.cs`; Infrastructure `BulkOperationServiceTests.cs`, `SdkContractTests.cs`; Platform `StartupAndSingleInstanceTests.cs`, `UpdateServiceTests.cs`.

### Verification

All commands used SDK `10.0.400` from `/tmp/cloudflare-r2-uploader-dotnet`, `DOTNET_CLI_HOME=/tmp/cloudflare-r2-uploader-cli-home`, `NUGET_PACKAGES=/tmp/cloudflare-r2-uploader-nuget`, telemetry/first-time/workload notifications disabled, `NUGET_CERT_REVOCATION_MODE=offline`, and `DOTNET_NOLOGO=1`.

```bash
/tmp/cloudflare-r2-uploader-dotnet/dotnet clean CloudflareR2Uploader.sln \
  -c Release -p:EnableWindowsTargeting=true -m:1 --nologo --verbosity:minimal

/tmp/cloudflare-r2-uploader-dotnet/dotnet restore CloudflareR2Uploader.sln \
  -p:EnableWindowsTargeting=true -p:NuGetAudit=false \
  -p:RestoreIgnoreFailedSources=true --disable-parallel -m:1 --verbosity minimal

/tmp/cloudflare-r2-uploader-dotnet/dotnet build CloudflareR2Uploader.sln \
  -c Release --no-restore -p:EnableWindowsTargeting=true \
  -p:NuGetAudit=false -p:RestoreIgnoreFailedSources=true -m:1 -warnaserror

/tmp/cloudflare-r2-uploader-dotnet/dotnet test \
  tests/CloudflareR2Uploader.Core.Tests/CloudflareR2Uploader.Core.Tests.csproj \
  -c Release --no-build

/tmp/cloudflare-r2-uploader-dotnet/dotnet test \
  tests/CloudflareR2Uploader.Infrastructure.Tests/CloudflareR2Uploader.Infrastructure.Tests.csproj \
  -c Release --no-build
```

- Clean exited `0`.
- Restore reported all projects up to date and exited `0`; `-m:1` also prevents this sandbox from attempting an unavailable MSBuild node socket.
- The solution build compiled Core, Infrastructure.R2, Platform.Windows, WPF, Core.Tests, Infrastructure.Tests, Platform.Tests, and Wpf.Tests with `0 Warning(s), 0 Error(s)`.
- Core tests: `77/77` passed, 0 failed, 0 skipped.
- Infrastructure tests: `24/24` passed, 0 failed, 0 skipped.
- Platform.Windows, WPF, Platform.Tests, and Wpf.Tests all passed their supported Linux compile checks. Runtime execution of Windows-targeted suites remains a Windows CI responsibility.
- The original structural invariant still passes: no legacy source/test tree, old solution, .NET Framework target, compatibility property, or linked `Migrated` item remains.
- Four Linux WPF compiler `*_wpftmp.csproj` artifacts left after the successful build were removed. A final `find` and `git status --short --untracked-files=all` show no generated temporary project or untracked build output.
- `git diff --check` exits `0`.

### Self-review

- Reviewed every edit against its exact diagnostic and retained public signatures and outcomes.
- Checked exception parameter names, ordinal/current-culture comparison modes, hash encodings, stream bounds/progress accounting, cancellation flow, and object-key/path behavior for equivalence.
- Confirmed analyzer configuration is unchanged: no suppression, `NoWarn`, severity reduction, disabled analyzer, or output filter was added.
- Confirmed the package-version comment now describes the canonical WPF-only application.

### Concerns

None in product code. Windows-only runtime tests cannot execute on this Linux host; all corresponding projects compile cleanly and the existing Windows CI gate remains authoritative for runtime coverage.
