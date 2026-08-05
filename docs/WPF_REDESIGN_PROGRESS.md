# WPF redesign progress

Living log for the migration described in [WPF_MIGRATION_PLAN.md](WPF_MIGRATION_PLAN.md).
Screen mapping lives in [WPF_DESIGN_MAPPING.md](WPF_DESIGN_MAPPING.md).

## Current state at a glance

| Stack | Command | Result |
| --- | --- | --- |
| Legacy WinForms (Debug) | `MSBuild CloudflareR2Uploader.sln -t:Build -p:Configuration=Debug` | **succeeds** |
| Legacy WinForms (Release) | `MSBuild CloudflareR2Uploader.sln -t:Build -p:Configuration=Release` | **succeeds** |
| Legacy tests (Debug and Release) | `vstest.console.exe CloudflareR2Uploader.Tests.dll /Platform:x64` | **85 passed, 0 failed in each configuration** |
| Modern solution (Debug and Release) | `dotnet build CloudflareR2Uploader.Wpf.sln` | **succeeds, 0 warnings** |
| `CloudflareR2Uploader.Core.Tests` | `dotnet test` | **77 passed, 0 failed** |
| `CloudflareR2Uploader.Infrastructure.Tests` | `dotnet test` | **24 passed, 0 failed** |
| `CloudflareR2Uploader.Platform.Tests` | `dotnet test` | **12 passed, 0 failed** |
| `CloudflareR2Uploader.Wpf.Tests` | `dotnet test` | **9 passed, 0 failed** |
| Modern total | `dotnet test CloudflareR2Uploader.Wpf.sln` | **122 passed, 0 failed** |
| Release package | `Build-Release.ps1 -Version 1.0.0-validation` | **succeeds; one WPF setup EXE** |

> Automated validation was run on 4 August 2026 with .NET SDK 10.0.302 and Inno Setup 7.0.2.
> Desktop validation on 4 August 2026 covered Upload, Files/list/inspector, the real 240 px
> object menu, Activity, Connection settings and combined Transfer/Application settings.
> A configured R2 profile passed connection, list and image-preview checks; an earlier live
> single-file upload and download completed with verified size/ETag and produced Activity rows.

## Reference screen checklist

A screen is only ticked when it is wired to real functionality, its commands work, its
loading and error states work, keyboard navigation works, it builds, and its tests pass.

| Screen | Wired | Commands | States | Keyboard | Tests | Done |
| --- | :-: | :-: | :-: | :-: | :-: | :-: |
| R1 `01-files-browser` | ☑ | ☑ | ☑ | ☑ | ☑ | ☑ |
| R2 `02-file-inspector` | ☑ | ☑ | ☑ | ☑ | ☑ | ☑ |
| R3 `03-multi-selection-context-menu` | ☑ | ☑ | ☑ | ☑ | ☑ | ☑ |
| R4 `04-upload-queue` | ☑ | ☑ | ☑ | ☑ | ☑ | ☑ |
| R5 `05-activity-history` | ☑ | ☑ | ☑ | ☑ | ☑ | ☑ |
| R6 `06-settings-connection` | ☑ | ☑ | ☑ | ☑ | ☑ | ☑ |
| R6b `07-settings-transfers-application` | ☑ | ☑ | ☑ | ☑ | ☑ | ☑ |
| R7 `08-design-system-overview` | ☑ | – | – | – | ☑ | ☑ |
| R8 `09-design-system-components` | ☑ | – | – | – | ☑ | ☑ |
| R9 `10-design-system-composites` | ☑ | – | – | – | ☑ | ☑ |

R7–R9 are consumed by the production screens and covered by a construction test that loads
every application resource dictionary and production view on an STA thread.

## Feature parity checklist

| Capability | WinForms | WPF | Notes |
| --- | :-: | :-: | --- |
| Bucket profiles with separate DPAPI credentials | ✔ | ✔ | real seven-section Settings window |
| Single-part upload | ✔ | ✔ | shared uploader and WPF queue |
| Multipart upload, bounded concurrency | ✔ | ✔ | shared uploader and resumable state |
| Multipart resume after restart | ✔ | ✔ | `UploadStateStore` shared unchanged |
| Pause / resume / cancel with part preservation | ✔ | ✔ | coordinated across window and tray exit |
| Retry failed, per-item retry | ✔ | ✔ | row and toolbar commands |
| Overwrite policies ask / overwrite / skip / rename | ✔ | ✔ | WPF prompt and queue integration |
| Post-upload verification (length + ETag) | ✔ | ✔ | shared service unchanged |
| Paginated browser with continuation tokens | ✔ | ✔ | real Files screen |
| Current-page filter (no request per keystroke) | ✔ | ✔ | 250 ms debounce |
| Typed sort, folders first | ✔ | ✔ | shared `BrowserViewService` |
| Multi-selection + Explorer right-click semantics | ✔ | ✔ | selection bridge and context menu |
| Keyboard shortcuts | ✔ | ✔ | shell and Files input bindings |
| Bulk download with preflight, dedupe, safe path mapping | ✔ | ✔ | wired to shared bulk service |
| Bulk delete with preflight | ✔ | ✔ | wired with confirmation |
| Rename / move / copy including folder prefixes | ✔ | ✔ | wired to object operations |
| New folder | ✔ | ✔ | wired |
| Public URL copy | ✔ | ✔ | wired |
| Temporary presigned links | ✔ | ✔ | URL retained only for dialog lifetime |
| Object properties / metadata | ✔ | ✔ | cancellable inspector |
| Safe preview | ✔ | ✔ | bounded authenticated preview with stale-result rejection |
| Preview cache bounded + randomised paths | ✔ | ✔ | shared service, unchanged |
| Activity history | ✖ | ✔ | sanitised persistence, filters, CSV export and clear |
| Settings dialog with validation | ✔ | ✔ | draft editing and dirty tracking |
| Connection test | ✔ | ✔ | shell header and Settings footer |
| Theme selection | ✖ | ✔ | dark, light and system modes |
| Tray icon, menu, balloon notifications | ✔ | ✔ | real `NotifyIcon` lifetime |
| Close/minimise to tray | ✔ | ✔ | window close coordinator |
| Start with Windows (`--background`) | ✔ | ✔ | shared service, unchanged |
| Single instance activation (`--show`) | ✔ | ✔ | shared service, unchanged |
| Active-upload exit confirmation | ✔ | ✔ | same coordinator used by exit and update |
| Update check + verified download | ✔ | ✔ | automatic/manual check, verified download, consented install |
| Per-user install, no admin | ✔ | ✔ | Inno packages the framework-dependent WPF publish |
| Settings migration from the WinForms build | – | ✔ | **complete and tested** |

Legend: ✔ done · ◐ partially done · ☐ not started · ✖ not present.

---

## Phase 1 — Audit, documentation, solution scaffold

**Status:** complete.

### Completed work

* Read the whole repository: solution, both projects, all 85 tests, `build/Build-Release.ps1`,
  `build/Generate-BrandIcon.ps1`, `installer/CloudflareR2Uploader.iss`,
  `deploy/Publish-CloudflareUpdate.ps1`, both GitHub workflows, `README.md`,
  `THIRD-PARTY-NOTICES.md`, `CHANGELOG.md`, `docs/RELEASING.md`.
* Inspected all ten Paper references and recorded tokens, densities and per-screen structure.
* Classified every source file and inventoried the WinForms, `System.Drawing`, `Invoke`,
  `MessageBox`, timer, registry, DPAPI, `NotifyIcon`, WebView2, Inno Setup and Fody/Costura
  dependencies.
* Measured the baseline: legacy Debug build succeeds; 85/85 tests pass.
* Wrote `WPF_MIGRATION_PLAN.md`, `WPF_DESIGN_MAPPING.md` and this log.
* Created `CloudflareR2Uploader.Wpf.sln` with `build/Modern.props`, `build/ModernTests.props`
  and eight SDK-style projects.

### Decisions made

1. **Two solutions.** `dotnet` cannot build the legacy non-SDK project, so the modern stack
   gets `CloudflareR2Uploader.Wpf.sln`. The legacy solution keeps its existing role.
2. **Shared source, single source of truth.** A `net48` project cannot reference a `net10.0`
   project, so shared services physically live in the new projects and the legacy csproj
   compiles them through linked `<Compile>` items. One copy of every service exists on disk.
3. **C# 7.3 subset for shared files**, enforced by the legacy compiler. New files opt into
   modern C# with a per-file `#nullable enable`.
4. **`build/Modern.props` is imported explicitly** rather than being a root
   `Directory.Build.props`, because the legacy non-SDK project imports
   `Microsoft.Common.props` and would otherwise inherit it.
5. **Costura/Fody dropped**, `PublishSingleFile` not adopted.
6. **Credential-storage label corrected**: the mock says "Use Windows Credential Manager";
   the implementation is DPAPI, so the UI will say DPAPI.

---

## Phase 2 — Core, Infrastructure.R2, Platform.Windows extraction

**Status:** complete.

### Completed work

* Moved 62 source files with `git mv` into the three new projects, preserving history:
  * `Core`: 21 models, 11 services, 12 utilities.
  * `Infrastructure.R2`: 16 R2 services plus the new `ImageDimensionReader`.
  * `Platform.Windows`: DPAPI, startup registration, single instance, updates, shell launcher.
* Rewired `src/CloudflareR2Uploader/CloudflareR2Uploader.csproj` to compile those files as
  links, so both front ends share one implementation.
* Split `UserInteractionServices.cs`: abstractions to `Core`, `WindowsProcessLauncher` to
  `Platform.Windows`, the WinForms clipboard to the WinForms project.
* Removed the last two front-end couplings from shared code:
  * `R2PreviewService` no longer uses `System.Drawing`; image dimensions come from the new
    `ImageDimensionReader`, which reads only the header through `MetadataExtractor`.
  * `UpdateService` no longer calls `Program.GetVersion()`; it takes an optional version and
    falls back to the entry assembly.
* Added settings schema versioning: `AppSettings.SchemaVersion` plus 13 schema-2 fields, and
  `SettingsMigrator` with a documented 1 → 2 upgrade that reproduces the WinForms behaviour
  exactly. `SettingsService.Load` runs it and keeps a one-off `settings.json.schema1.bak`.
* Added the activity-history feature required by R5:
  `ActivityRecord`, `IActivityHistoryService`, `ActivityHistoryService` (JSON Lines, atomic
  rewrites, corruption recovery, 4 MB / 5000-record bound, retention pruning),
  `ActivitySanitizer` and `ActivityCsvExporter`.
* Migrated all 85 legacy tests by linking them into the three new test projects, split by
  layer. 78 moved unchanged; the 7 in `UiConstructionTests` are WinForms-only and stay with
  the legacy project until it is deleted.
* Added 27 new tests: 14 for settings migration and schema round-tripping, 19 for activity
  history and sanitisation, 7 for CSV quoting.

### Files changed

62 files moved; `src/CloudflareR2Uploader/CloudflareR2Uploader.csproj`;
`Core/Models/AppSettings.cs`, `AppTheme.cs`, `ActivityRecord.cs`;
`Core/Services/SettingsService.cs`, `SettingsMigrator.cs`, `ActivityHistoryService.cs`,
`ActivitySanitizer.cs`, `ActivityCsvExporter.cs`, `IActivityHistoryService.cs`,
`ISystemThemeProvider.cs`, `UserInteractionAbstractions.cs`;
`Core/Utilities/AppPaths.cs`;
`Infrastructure.R2/Services/ImageDimensionReader.cs`, `R2PreviewService.cs`;
`Platform.Windows/Services/UpdateService.cs`, `WindowsProcessLauncher.cs`,
`WindowsThemeProvider.cs`; `src/CloudflareR2Uploader/Services/WinFormsClipboardService.cs`;
three test `.csproj` files and three new test files.

### Tests added

`SettingsMigrationTests` (14), `ActivityHistoryServiceTests` (19), `ActivityCsvExporterTests` (7).

Notable coverage: a real 1.0.0-era `settings.json` migrates without losing a single field or
bucket profile; migration is idempotent and refuses to downgrade a newer file; a signed URL
recorded as an activity target keeps its object path but loses its entire query string; a
64-hex secret and a SigV4 `Credential=` token are redacted; control characters are stripped
so a record cannot forge an extra JSON or CSV line; CSV quoting handles commas, quotes,
newlines, significant whitespace and spreadsheet formula prefixes.

### Commands run

| Command | Result |
| --- | --- |
| `MSBuild CloudflareR2Uploader.sln -t:Build -p:Configuration=Debug` | success |
| `MSBuild CloudflareR2Uploader.sln -t:Build -p:Configuration=Release` | success |
| `vstest.console.exe CloudflareR2Uploader.Tests.dll /Platform:x64` | 85 passed |
| `dotnet build src\CloudflareR2Uploader.Platform.Windows\...` | success, 0 warnings |
| `dotnet test tests\CloudflareR2Uploader.Core.Tests\...` | 77 passed |
| `dotnet test tests\CloudflareR2Uploader.Infrastructure.Tests\...` | 24 passed |
| `dotnet test tests\CloudflareR2Uploader.Platform.Tests\...` | 12 passed |

### Decisions made

1. **Analyzer suppressions are scoped, not global.** Projects that carry linked C# 7.3 files
   set `SharedSourceWithLegacy=true`, which suppresses 21 "use the newer idiom" rules and
   nothing else. Correctness analyzers stay on. The list is deleted with the WinForms project.
2. **Activity history is JSON Lines**, not a JSON array: appending is a plain append, and a
   crash mid-write can only damage the final line, which the reader skips.
3. **The activity file is treated as a publication surface** and is sanitised more strictly
   than the log: it additionally strips bare `Credential=` tokens, which
   `LoggingService.Sanitize` leaves behind when it truncates an Authorization header.
4. **Query strings are stripped before credential redaction** for URL-shaped targets, so an
   activity row keeps the object path the screen exists to show while the signature is
   discarded. Running the redaction first would replace the whole URL with a placeholder.

### Known limitations

None outstanding for this phase.

---

## Phase 3 — Design system and shell

**Status:** complete.

### Completed work

* **Design tokens.** `Theme.Dark.xaml` carries all 21 Paper colour tokens verbatim.
  `Theme.Light.xaml` is a derived light palette under identical keys — the reference only
  specifies dark, so it inverts the surface stack in the same five steps, keeps the accent
  hue, and darkens each status colour to hold contrast. Every brush is frozen.
* **Typography** (`Typography.xaml`) with the eight styles from the reference scale and the
  Inter / JetBrains Mono fallback chains. No font files are packaged.
* **Spacing and metrics** (`Spacing.xaml`): 4 px step, the five radii, 28/30/32 px control
  heights, 30 px rows, 380 dip inspector, 96 px metadata label lane.
* **Icons** (`Icons.xaml`): 45 stroke geometries on a 24 × 24 grid, frozen.
* **Component dictionaries**, all matching Images/08–10:
  `Controls.Focus`, `Controls.Buttons`, `Controls.Inputs`, `Controls.Toggles`,
  `Controls.Navigation`, `Controls.Badges`, `Controls.Progress`, `Controls.ScrollBars`,
  `Controls.Tables`, `Controls.Menus`, `Controls.Dialogs`, `Controls.Tooltips`,
  `Controls.Placeholders`, `Controls.Metadata`.
* **Attached properties** `Controls/Icon.cs` and `Controls/Placeholder.cs`, so one control
  template renders any glyph and text inputs get real placeholders.
* **`App.xaml`** merging all dictionaries in dependency order, with the palette pinned at
  index 0 so it can be swapped, plus application-wide defaults and converter instances.
* **`Converters/ValueConverters.cs`**: nine converters including an invertible
  boolean-to-visibility and an `EnumMatchConverter` whose `ConvertBack` refuses to write on
  uncheck, so clicking a segmented control can never momentarily select nothing.
* **`ThemeService`** swapping one merged dictionary; **`WindowsThemeProvider`** polling the
  `AppsUseLightTheme` preference so the System option follows Windows live.
* **`WindowChromeInterop`** forcing the dark DWM caption, with the reasoning for keeping the
  real caption instead of a custom `WindowChrome` recorded in the file.
* **`AppSession`**: the live settings/credential/profile state, handing out clones and a
  `ProfileToken` that makes stale-result rejection possible across profile switches.
* **`ToastService`**, **`WpfClipboardService`**, **`ApplicationInfo`**.
* **`IDialogService` + `DialogService`**, covering messages, confirmations with an optional
  opt-in, text prompts, the overwrite prompt, the keep-parts-on-cancel choice, the temporary
  link dialog, settings, and the three native file pickers.
* **Dialog view models and windows**: `MessageDialog`, `TextPromptDialog`,
  `OverwritePromptDialog`, `TemporaryLinkDialog`, on a shared `ThemedDialogWindow` that
  traps Tab, forces the dark caption, and converts `CloseRequested` into `DialogResult`.
* **`ShellViewModel`** with navigation, the bucket selector, and a cancellable header
  connection check that discards a result belonging to a superseded profile.
* **`FilesViewModel`** (Phase 4 work, started early because the shell depends on it):
  listing with continuation tokens, 250 ms debounced current-page filtering, typed sorting,
  breadcrumbs, pagination, selection facts driving command enablement, and real
  implementations of copy keys, copy/open public URL, temporary link, new folder, rename,
  move, delete and bulk download, each recording a sanitised activity entry.
* **`FileRowViewModel`** and **`FileInspectorViewModel`** including the multiple-selection
  variant, the version-token scheme that rejects stale metadata and preview responses, and
  the metadata group construction for Overview and Metadata.
* **Production composition root and shell views**: dependency injection, initial-page
  activation, tray-owned lifetime, all page data templates, runtime theme resources and
  exception logging are wired in `App.xaml.cs` and `MainWindow`.

### Commands run

| Command | Result |
| --- | --- |
| `dotnet build CloudflareR2Uploader.Wpf.sln -c Debug` | **passes, 0 warnings** |
| `dotnet build CloudflareR2Uploader.Wpf.sln -c Release` | **passes, 0 warnings** |
| `dotnet test CloudflareR2Uploader.Wpf.sln` | **122 passed, 0 failed** |

### Remaining work

None outstanding for this phase.

### Known limitations

* The light palette is derived because the supplied references define dark mode only.
* Native WebView2 PDF rendering still depends on the Evergreen Runtime being installed;
  the inspector degrades to its explicit unsupported state when it is unavailable.

---

## Phase 4 — Files browser

**Status:** complete. The real paginated list, breadcrumbs, filter/sort controls, selection
bridge, context menu, keyboard bindings, bulk operations and empty/error states are wired.

## Phase 5 — File inspector

**Status:** complete. Single and multiple selection, metadata, sharing, authenticated bounded
preview, cancellation and stale-result rejection are implemented.

## Phase 6 — Upload screen

**Status:** complete. Drag/drop, native pickers, destination options, real upload queue,
per-item actions, multipart progress and aggregate controls use the shared upload services.

## Phase 7 — Activity history

**Status:** complete. The production screen reads the sanitised store and supports grouping,
filters, search, retention, CSV export and confirmed clearing without sample data.

## Phase 8 — Settings

**Status:** complete. Connection, buckets, transfers, application, notifications, updates and
about sections edit a draft, validate inline and write only on Save. Password binding remains
in the view code-behind so secret material never becomes a dependency property.

## Phase 9 — Tray, startup, updates, installer, CI

**Status:** complete. The WPF process owns the tray lifetime, startup registration,
single-instance activation and coordinated real exit. Automatic/manual update checks lead to
an exact-length and SHA-256-verified download and explicit install consent. The release script
publishes `win-x64` WPF framework-dependent files, Inno requires the .NET 10 Desktop Runtime,
and CI/release workflows provision .NET 10 and package only the WPF installer.

## Phase 10 — Parity, accessibility, DPI, performance, docs

**Status:** complete for automated and local smoke-test scope. All production views load their
resources under an STA test; view-model tests cover validation, dirty state, command state,
selection summaries and upload status mapping. Debug and Release each build with zero warnings
and pass 122 modern tests; the retained compatibility solution still passes 85 tests in both
configurations. The release script completed with tests and produced one installer EXE.

### Validation boundary

The portable Debug executable was used for the desktop pass; no installed copy was required.
Live connection/list/preview checks ran against the `storage` profile. A live upload/download
round trip is recorded on the configured `gfnos` profile. Remote delete, multipart
cancellation/resume, rename/move, startup-login, silent in-place update and final installer
execution were not performed in this pass.

---

## Post-implementation review pass

A running-application review against the Paper references found and fixed four defects that
a build-and-unit-test pass could not have caught.

### 1. Pill controls rendered as ellipses

`Radius.Pill` was `999`, borrowed from the CSS idiom for a stadium. A WPF `Border` does not
behave that way: it clamps each corner to half the width **and** half the height
independently, so an oversized radius produces elliptical corners that meet in the middle.
Filter chips, count badges, toggle tracks, progress bars and scrollbar thumbs were therefore
drawn as ovals instead of the stadiums the design specifies.

Fixed by replacing the single oversized token with one radius per control, each exactly half
that control's height: `Radius.Pill` 13 (26 px chips), `Radius.PillFocus` 15,
`Radius.Badge` 8, `Radius.ToggleTrack` 10, `Radius.ToggleThumb` 7, `Radius.Bar` 2 and
`Radius.ScrollThumb` 3. The reasoning is recorded in `Themes/Spacing.xaml` so the tokens are
not "simplified" back to a single value.

### 2. Five inspector and preview buttons did nothing

`FileInspectorViewModel` and `PreviewViewModel` correctly raise intent rather than acting —
neither holds the selection, the R2 services or the dialogs. Nothing subscribed, so
`CloseRequested`, `ActionRequested`, `DownloadRequested` and `RetryRequested` were raised into
the void. The inspector's close, Download, Copy key and Create temporary link buttons, and the
preview's Download and Try again buttons, all looked enabled and did nothing. The Files
toolbar's Upload button was dead for the same reason (`UploadPageRequested`).

Fixed by subscribing in the owners: `FilesViewModel` handles the inspector and preview
requests, `ShellViewModel` handles the page-change request, and both unsubscribe on dispose.

An unsubscribed event raises no error, so `InspectorWiringTests` now guards it: three
behavioural tests plus one that asserts every one of those events has a live subscriber once
the Files screen is constructed.

### 3. Empty upload queue was left-aligned

The empty-state block was an undocked `DockPanel` child, and an undocked child docks Left, so
`HorizontalAlignment="Center"` had nothing to centre within. Fixed with an explicit
`DockPanel.Dock="Top"`.

### 4. Settings footer showed a raw enum

Before any connection test had run, the footer chip read "Unknown" — the `ConnectionState`
enum name, which means nothing to a user. The chip is now hidden until a test has run and is
coloured by outcome, with the outcome always spelled out in words as well as colour.

### 5. A source asset lived in the disposable build-output folder

`README.md`'s screenshot was tracked at `artifacts/cloudflare-r2-uploader.png`, but
everything else under `artifacts/` is generated output that a clean build is expected to
delete. Wiping the folder to force a clean build therefore destroyed a tracked source file,
and `Build-Release.ps1` then failed several minutes in with a raw `Copy-Item` path error.

Fixed structurally rather than by restoring the file:

* the image moved to `docs/images/cloudflare-r2-uploader.png`, with `README.md` and the
  release script updated to match;
* `.gitignore` now ignores `artifacts/` in full, so nothing tracked can live there again;
* the payload keeps the image at its repository-relative path so the README's own link still
  resolves from the install directory;
* the script validates every required repository input up front and fails in about a second
  with the missing paths and how to restore them, instead of part-way through the run.

Verified by deleting `artifacts/` and `dist/` outright and re-running the release script: it
completes, runs 126 tests and produces one installer.

### Known documentation gap

`docs/images/cloudflare-r2-uploader.png` still shows the WinForms window and is stale after
the redesign. Regenerating it needs a capture from a profile with no real bucket or account
in frame, so it was left for the owner to decide rather than published with live account
details.

### Commands run in this pass

| Command | Result |
| --- | --- |
| `dotnet build CloudflareR2Uploader.Wpf.sln -c Debug` | success, 0 warnings |
| `dotnet build CloudflareR2Uploader.Wpf.sln -c Release` | success, 0 warnings |
| `dotnet test CloudflareR2Uploader.Wpf.sln -c Debug` | 126 passed (24 + 12 + 77 + 13) |
| `dotnet test CloudflareR2Uploader.Wpf.sln -c Release` | 126 passed |
| `MSBuild CloudflareR2Uploader.sln -t:Build -p:Configuration=Debug` | success |
| `vstest.console.exe CloudflareR2Uploader.Tests.dll /Platform:x64` | 85 passed |
| `build\Build-Release.ps1 -Version 1.0.0-dev` after deleting `artifacts/` and `dist/` | 126 passed, one installer produced (4,250,053 bytes) |

Desktop verification for this pass drove the running application: Upload, Files, inspector,
Activity and Settings were captured and compared against `Images/`, chip geometry was compared
against `Images/05-activity-history.png` at 3× magnification, and the inspector close button
was exercised to confirm the panel actually hides.
