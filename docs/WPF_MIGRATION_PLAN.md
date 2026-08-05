# WPF migration plan

Migration of Cloudflare R2 Uploader from Windows Forms on .NET Framework 4.8 to WPF on
.NET 10, implementing the Paper design stored in `Images/`.

Branch: `feature/wpf-redesign`. Baseline commit: `aad498e` (Cloudflare R2 Uploader 1.0.0).

## 1. Baseline measured before any change

| Check | Command | Result |
| --- | --- | --- |
| Legacy restore | `MSBuild CloudflareR2Uploader.sln -t:Restore -p:Configuration=Debug` | success |
| Legacy build | `MSBuild CloudflareR2Uploader.sln -t:Build -p:Configuration=Debug` | success |
| Legacy tests | `vstest.console.exe tests\...\CloudflareR2Uploader.Tests.dll /Platform:x64` | 85 passed, 0 failed |
| Toolchain | `dotnet --info` | SDK 10.0.302, `Microsoft.WindowsDesktop.App 10.0.10` present |
| Legacy toolchain | `vswhere` | Visual Studio at `D:\VisualStudio`, .NET Framework 4.8 targeting pack present |

The legacy build must keep passing these exact checks for the whole migration. It is the
fallback implementation until WPF parity is signed off.

## 2. Current architecture (audit)

`src/CloudflareR2Uploader` is one non-SDK `net48` WinExe with 23,260 lines of C# 7.3.
Everything — models, R2 access, Windows integration and WinForms UI — lives in that one
assembly. `tests/CloudflareR2Uploader.Tests` is a non-SDK `net48` MSTest library with
`InternalsVisibleTo` access to it.

### 2.1 Classification of every existing source file

**Pure application / domain logic** (no AWS SDK, no WinForms, no Windows API):

| File | Role |
| --- | --- |
| `Models/AppSettings.cs` | settings contract, clamping, bucket-profile normalisation |
| `Models/R2BucketProfile.cs` | non-secret per-bucket connection record |
| `Models/R2Credentials.cs` | credential pair, log-safe `ToString` |
| `Models/R2BrowserItem.cs`, `R2BrowserPage.cs`, `R2BrowserBreadcrumbSegment.cs`, `BrowserModels.cs` | browser rows, page and sort/filter state |
| `Models/BulkOperationModels.cs` | bulk plan/progress/result records |
| `Models/CompletedPartState.cs`, `MultipartUploadState.cs` | resumable multipart state contract |
| `Models/OverwriteBehavior.cs`, `UploadItemStatus.cs`, `UploadProgressInfo.cs`, `OverallProgressInfo.cs`, `UploadQueueItem.cs`, `UploadResult.cs` | upload queue domain |
| `Models/ConnectionTestResult.cs`, `DetailsPreviewModels.cs` | connection test and object-properties/preview contracts |
| `Services/BrowserViewService.cs` | filter + typed sort + selection summary |
| `Services/MimeTypeService.cs` | extension → content type |
| `Services/PauseController.cs` | real pause gate |
| `Services/ILoggingService.cs` | log abstraction |
| `Services/LoggingService.cs` | daily sanitised log file writer + `Sanitize` |
| `Services/SettingsService.cs` | atomic `settings.json` load/save |
| `Services/UploadStateStore.cs` | resumable state persistence + `CanResume` |
| `Services/PreviewCacheService.cs` | bounded randomised preview cache |
| `Services/BulkLocalPathMapper.cs` | key → safe Windows relative path |
| `Services/SemanticVersion.cs` | SemVer parse/compare |
| `Utilities/*` except `UiThreadUtility.cs`, `GraphicsExtensions.cs` | keys, prefixes, pagination, formatters, multipart maths, throttle, speed, paths, bounded stream, app paths |

**R2 infrastructure** (depends on `AWSSDK.S3` / `Amazon.Runtime`):

`Services/R2ClientFactory.cs`, `R2ConnectionTester.cs`, `R2ObjectBrowserService.cs`,
`R2BrowserResponseMapper.cs`, `R2ObjectOperationService.cs`, `R2BulkOperationService.cs`,
`R2ObjectDetailsService.cs`, `R2PresignedUrlService.cs`, `R2PreviewService.cs`,
`R2MultipartUploader.cs`, `R2SinglePartUploader.cs`, `R2UploadService.cs`,
`UploadQueueService.cs`, `RetryService.cs`, `TransientErrorClassifier.cs`,
`FriendlyErrorService.cs`.

**Windows-specific infrastructure**:

`Services/CredentialProtectionService.cs` (DPAPI `ProtectedData`, `CurrentUser`),
`Services/StartupRegistrationService.cs` (`HKCU\...\Run`),
`Services/SingleInstanceCoordinator.cs` (named mutex + named pipe + `WindowsIdentity`),
`Services/UpdateService.cs` (HTTPS manifest, SHA-256 verified installer download),
`Services/UserInteractionServices.cs` (WinForms `Clipboard`, `Process.Start`),
`Tray/TrayApplicationContext.cs` (`NotifyIcon`, WinForms timer, `ApplicationContext`).

**WinForms-specific UI** (replaced, not migrated):

`Program.cs`, all of `Forms/`, all of `Controls/`, all of `Theming/`,
`Utilities/UiThreadUtility.cs`, `Utilities/GraphicsExtensions.cs`.

### 2.2 Dependency inventory requested by the audit

| Dependency | Where | Migration decision |
| --- | --- | --- |
| `System.Windows.Forms` | `Program`, `Forms/*`, `Controls/*`, `Theming/*`, `Tray/TrayApplicationContext`, `Utilities/UiThreadUtility`, `Services/UserInteractionServices` | not carried over. WPF `App`/`Views`, `Dispatcher`, WPF `Clipboard`, and a WPF-hosted tray icon replace them. |
| `System.Drawing` | `Controls/*`, `Theming/*`, `Program.LoadApplicationIcon`, `Services/R2PreviewService.ExtractImageDetails` | only the preview use sits in shared code. `System.Drawing.Common` is Windows-only and an extra package on .NET 10, so image dimensions now come from `MetadataExtractor`, which is already a dependency. Everything else is WinForms-only and is dropped. |
| `Control.Invoke` / `BeginInvoke` | `Utilities/UiThreadUtility`, `Tray/TrayApplicationContext`, `Forms/MainForm*` | replaced by `Dispatcher`, `IProgress<T>` captured on the UI `SynchronizationContext`, and an explicit `IUiDispatcher` abstraction in `Core`. |
| `MessageBox` | `Program`, `Forms/*`, `Tray/TrayApplicationContext` | replaced by an `IDialogService` with real WPF dialogs styled to the Paper design. |
| WinForms timers | `Tray/TrayApplicationContext` (update delay), `MainForm.BrowserEnhancements` (250 ms filter and selection debounce) | replaced by `DispatcherTimer` inside view models/services. Debounce intervals are preserved at 250 ms. |
| Windows registry | `Services/StartupRegistrationService` only (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`) | moved verbatim into `Platform.Windows`; still per-user, still no administrator rights. |
| DPAPI | `Services/CredentialProtectionService` (`ProtectedData`, `DataProtectionScope.CurrentUser`, fixed entropy, `R2CRED1`/`R2CRED2` frames) | moved verbatim into `Platform.Windows`. Blob format is unchanged so existing `credentials.bin` files keep working. |
| `NotifyIcon` | `Tray/TrayApplicationContext` | WPF has no tray control. `TrayIconService` is the one WPF-layer adapter that wraps `System.Windows.Forms.NotifyIcon` from the Windows Desktop pack. The project enables WinForms only for this shell integration; application logic remains behind Windows-specific interfaces. This keeps Shell_NotifyIcon behaviour, balloon tips and the Windows 11 overflow menu without hand-rolling P/Invoke. |
| WebView2 | `Controls/R2DetailsPanel` (PDF/rich preview) | `Microsoft.Web.WebView2` ships a WPF control in the same package. Kept, with the same hardening: fixed user-data folder, no navigation to signed or public R2 URLs, remote navigation and popups blocked. |
| Inno Setup | `installer/CloudflareR2Uploader.iss`, `build/Build-Release.ps1` | kept. Payload switches to the framework-dependent .NET 10 publish output of the WPF app; the release is still exactly one setup EXE. |
| Fody + Costura | `FodyWeavers.xml`, `CloudflareR2Uploader.csproj` | **dropped for the WPF app** — see §6. |

### 2.3 Existing tests

All 85 tests are deterministic and offline. They are re-targeted to the new projects
without behavioural change:

| Test file | New home | Note |
| --- | --- | --- |
| `FileSizeFormatterTests`, `MultipartCalculatorTests`, `ObjectKeyUtilityTests`, `PathUtilityTests`, `R2BrowserPathUtilityTests`, `R2BrowserPaginationStateTests`, `R2ObjectOperationPathUtilityTests`, `BrowserViewServiceTests`, `MimeTypeServiceTests`, `SettingsServiceTests`, `UploadStateStoreTests`, `PreviewCacheServiceTests`, `BulkLocalPathMapperTests`, `ProgressAggregationTests` | `CloudflareR2Uploader.Core.Tests` | unchanged assertions |
| `RetryServiceTests`, `TransientErrorClassifierTests`, `R2BrowserResponseMapperTests`, `PreviewServiceTests`, `UrlServiceTests`, `SdkContractTests`, `BulkOperationServiceTests`, `UploadQueueServiceTests` | `CloudflareR2Uploader.Infrastructure.Tests` | unchanged assertions |
| `CredentialProtectionTests`, `StartupAndSingleInstanceTests`, `UpdateServiceTests` | `CloudflareR2Uploader.Platform.Tests` | unchanged assertions |
| `UiConstructionTests` | replaced | asserts WinForms control construction only; superseded by real view-model tests in `CloudflareR2Uploader.Wpf.Tests`. The legacy copy stays in the legacy test project until the WinForms project is deleted. |

The legacy `tests/CloudflareR2Uploader.Tests` project stays in place and keeps running
against the legacy front end for the whole migration.

## 3. Target architecture

```
src/
  CloudflareR2Uploader.Core/                net10.0        domain, state, formatters, abstractions
  CloudflareR2Uploader.Infrastructure.R2/   net10.0        AWSSDK.S3 access, upload/queue, preview
  CloudflareR2Uploader.Platform.Windows/    net10.0-windows DPAPI, registry, tray, single instance, updates
  CloudflareR2Uploader.Wpf/                 net10.0-windows WPF shell, views, view models, design system
  CloudflareR2Uploader/                     net48          existing WinForms front end (kept until parity)
tests/
  CloudflareR2Uploader.Core.Tests/          net10.0
  CloudflareR2Uploader.Infrastructure.Tests/net10.0
  CloudflareR2Uploader.Platform.Tests/      net10.0-windows
  CloudflareR2Uploader.Wpf.Tests/           net10.0-windows
  CloudflareR2Uploader.Tests/               net48          existing legacy tests (kept until parity)
```

Reference direction is strictly one-way:

```
Wpf ──► Platform.Windows ──► Infrastructure.R2 ──► Core
 └──────────────────────────────────┴──────────────►┘
```

`Core` references nothing. Nothing references `Wpf`.

### 3.1 How code is shared with the still-live WinForms app

A `net48` project cannot reference a `net10.0` project, so assembly-level sharing is
impossible while both front ends exist. Duplicating the services would mean two
implementations to keep in step, which is exactly what must not happen.

The migration therefore uses **shared source with a single source of truth**. Each shared
file physically lives in exactly one new project directory. The legacy
`CloudflareR2Uploader.csproj` compiles the same files through linked items:

```xml
<Compile Include="..\CloudflareR2Uploader.Core\Services\SettingsService.cs">
  <Link>Services\SettingsService.cs</Link>
</Compile>
```

Consequences, all deliberate:

* There is one copy of every shared service on disk. A fix lands in both front ends.
* Namespaces are unchanged (`CloudflareR2Uploader.Models`, `.Services`, `.Utilities`), so
  no legacy call site is edited.
* Shared files stay inside the C# 7.3 subset the legacy project compiles with. New files
  that only the modern projects compile opt in per file with `#nullable enable` and use
  current C#. The modern projects therefore set `<Nullable>disable</Nullable>` at project
  level and enable it per new file, rather than drowning moved legacy files in warnings.
* `internal` members keep working in both directions because each project compiles its own
  assembly from the same text; `InternalsVisibleTo` is declared per project.

This bridge is temporary. When the WinForms project is deleted the linked items go with
it, project-level `<Nullable>enable</Nullable>` is turned on, and the C# 7.3 constraint is
lifted. That step is tracked in `WPF_REDESIGN_PROGRESS.md` Phase 10.

### 3.1a Shared MSBuild settings

`build/Modern.props` is imported explicitly by each SDK-style project rather than being
placed in a root `Directory.Build.props`. The legacy non-SDK project imports
`Microsoft.Common.props`, which would pull a root file in and apply modern properties to a
`net48` C# 7.3 project. The explicit import keeps the two toolchains cleanly separated.

Projects that carry linked C# 7.3 files set `SharedSourceWithLegacy=true`, which suppresses
21 "use the newer idiom" analyzer rules — and nothing else. Correctness analyzers stay on.
The list is removed together with the WinForms project.

### 3.2 Solution files

`dotnet build` cannot build the legacy non-SDK project, so there are two solutions:

* `CloudflareR2Uploader.sln` — unchanged role: legacy WinForms app + legacy tests, retained
  as a compatibility gate and built directly with Visual Studio MSBuild.
* `CloudflareR2Uploader.Wpf.sln` — all SDK-style projects. This is what
  `dotnet restore/build/test` operates on and what CI gates on.

## 4. Changes forced on shared code, and why

Only three shared files change behaviourally-neutral details so they compile on both
target frameworks:

1. `Services/R2PreviewService.cs` — `System.Drawing.Image.FromFile` replaced by
   `ImageDimensionReader`, which reads width/height through `MetadataExtractor`. Same
   `PixelWidth`/`PixelHeight` output, no `System.Drawing.Common` dependency, works headless.
2. `Services/UpdateService.cs` — the constructor no longer calls `Program.GetVersion()`.
   It takes an optional application version and falls back to the entry assembly's
   informational version, so the service no longer depends on a front end.
3. `Services/UserInteractionServices.cs` — `IClipboardService` / `IProcessLauncher`
   interfaces and `PublicUrlService` move to `Core`; the WinForms implementations stay in
   the WinForms project, and WPF supplies its own.

Everything else moves byte-for-byte.

## 5. Settings and state migration

`settings.json` today has no version field. The reader must therefore treat "no version"
as schema 1 and never discard unknown data.

* A `schemaVersion` member is added, defaulting to `1` when absent.
* `SettingsMigrator` upgrades 1 → 2 by keeping every existing field, seeding the new WPF
  fields (`theme`, `activityRetentionDays`, `preferPublicUrls`, `temporaryLinkExpiryHours`,
  `verifyETagAfterUpload`, `continueTransfersWhileMinimised`, `confirmDeletes`, per-screen
  notification toggles) with values that reproduce today's behaviour.
* Legacy single-bucket fields keep mirroring the active profile exactly as
  `AppSettings.NormalizeBucketProfiles()` already does, so a downgrade to the WinForms
  build still works.
* `credentials.bin` format is untouched: `R2CRED1` legacy blobs still upgrade to the
  `R2CRED2` per-profile vault on first save.
* `UploadState\*.json` is untouched, so an interrupted multipart upload started in the
  WinForms build resumes in the WPF build and vice versa.

Nothing is written until the user presses Save, and a failed save leaves the previous file
in place.

## 6. Package decisions

| Package | Version | Project | Why |
| --- | ---: | --- | --- |
| `AWSSDK.S3` | 3.7.511.8 | Infrastructure.R2 | unchanged R2 S3 client. Same version as the legacy app so request shapes, `DisablePayloadSigning` and multipart behaviour are identical. |
| `MetadataExtractor` | 2.9.3 | Infrastructure.R2 | unchanged. Now also supplies image dimensions in place of `System.Drawing`. |
| `Newtonsoft.Json` | 13.0.4 | Infrastructure.R2, Platform.Windows | unchanged. Used for JSON preview pretty-printing and the update manifest. Kept rather than swapped to `System.Text.Json` because the same source must compile on `net48`. |
| `Microsoft.Web.WebView2` | 1.0.4078.44 | Wpf | unchanged version; the WPF control ships in the same package as the WinForms one. |
| `CommunityToolkit.Mvvm` | 8.4.0 | Wpf, Wpf.Tests | **new.** `ObservableObject`, `[ObservableProperty]`, `[RelayCommand]` and `IAsyncRelayCommand` remove several thousand lines of `INotifyPropertyChanged` and `ICommand` boilerplate. Source-generator based, no runtime UI dependency, no visual components. |
| `Microsoft.Extensions.DependencyInjection` | 10.0.0 | Wpf | **new.** Composition root only. Chosen over a hand-written container because service lifetimes (singleton services, transient view models, scoped-per-profile clients) are exactly what it expresses. |
| `MSTest.TestAdapter` / `MSTest.TestFramework` | 3.6.4 | all test projects | unchanged framework so migrated tests keep their attributes. |
| `Microsoft.NET.Test.Sdk` | 17.12.0 | all test projects | **new**, required for `dotnet test` on SDK-style projects. The legacy project used VSTest directly. |

Removed: `Fody` and `Costura.Fody`. Costura embedded managed dependencies into the single
`net48` EXE. On .NET 10 the application is a framework-dependent publish whose files are
installed side by side by Inno Setup, and Costura does not support .NET Core-style
assembly loading. It buys nothing: the *release* is still a single setup EXE, which is what
users see. `PublishSingleFile` is deliberately **not** used either — it interferes with the
WebView2 loader's native asset probing and with the updater relaunching a known path.
Reliability is preferred over one installed file, as instructed.

Removed: `Ookii.Dialogs.WinForms`. .NET 10's `Microsoft.Win32.OpenFolderDialog` is a real
IFileDialog-based folder picker, so the third-party package is no longer needed.

## 7. Risks and mitigations

| Risk | Mitigation |
| --- | --- |
| Editing shared source breaks the frozen WinForms build invisibly | the legacy solution is rebuilt and its 85 tests re-run after every phase that touches shared files; results recorded in the progress log |
| Shared files must stay C# 7.3-compatible | enforced by the legacy build; new code lives in files the legacy project does not link |
| `DataContractJsonSerializer` behaviour differs on .NET 10 | covered by round-trip tests over real `settings.json` and `MultipartUploadState` payloads in `Core.Tests` |
| AWS SDK v3 behaviour differs between `net48` and `net10.0` | `SdkContractTests` are carried over and extended; the pinned version is identical |
| WebView2 runtime absent | already handled today: PDF preview degrades to an "install the runtime" state; the same state is implemented in the WPF inspector |
| DPAPI blob unreadable after profile change | existing behaviour retained — returns null, the UI asks for credentials again |
| Tray via WinForms interop inside a WPF process | isolated to `TrayIconService`; it owns and disposes the `NotifyIcon`, while queue, startup, exit and update decisions remain in injected services |
| Installer regressions | `Build-Release.ps1` payload validation is extended to assert the .NET 10 publish contents (`.dll`, `.runtimeconfig.json`, `.deps.json`, WebView2 native loaders) before Inno Setup runs |
| Two front ends drift | shared source guarantees one implementation of every service; front-end-only code is what differs |

## 8. Compatibility concerns

* Windows 10 1809+ and Windows 11, x64. Unchanged `MinVersion=10.0` in the installer.
* .NET 10 Desktop Runtime becomes a prerequisite. The installer gains a check that points
  the user at the Microsoft download when it is missing; it does not silently install it.
* Per-user install under `%LocalAppData%\Programs\CloudflareR2Uploader` is unchanged, so
  `PrivilegesRequired=lowest` and the existing `AppId` stay, and an in-place upgrade from
  the WinForms build keeps `%LocalAppData%\CloudflareR2Uploader\` user data.
* The startup Run value and its `--background` argument are unchanged, so an existing
  registration keeps working after upgrade.

## 9. Phases

| Phase | Content | Exit condition |
| --- | --- | --- |
| 1 | audit, these documents, SDK solution scaffold | new solution restores and builds empty projects; legacy still green |
| 2 | move Core / Infrastructure.R2 / Platform.Windows source, settings migration, migrate tests | `dotnet test` green; legacy build + 85 tests still green |
| 3 | design tokens, resource dictionaries, reusable control templates, shell, navigation, theming | shell runs, all three pages reachable, theme switch works |
| 4 | Files browser | R1 screen wired to real listings with pagination, sorting, filtering, selection, context menu, shortcuts |
| 5 | File inspector | R2/R3 screens wired to real metadata, preview, sharing, multi-selection |
| 6 | Upload screen | R4 screen wired to the real queue, drag/drop, multipart progress |
| 7 | Activity history | R5 screen backed by a real sanitised store with export |
| 8 | Settings | R6/R6b wired to real settings, validation, connection test, DPAPI |
| 9 | tray, startup, single instance, updates, installer, CI | packaged installer produces a working WPF app |
| 10 | parity audit, accessibility, DPI, performance, docs | definition of done met |

## 10. Definition of done

Tracked item by item in `WPF_REDESIGN_PROGRESS.md`. The WinForms project is deleted only
after every parity row there is ticked.
