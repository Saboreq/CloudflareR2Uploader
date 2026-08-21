# WPF Modernization and Repository Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the retired .NET Framework WinForms application, make the .NET 10 WPF solution canonical, and fix every confirmed review finding with regression coverage and a verified Windows release build.

**Architecture:** Preserve the existing Core → Infrastructure.R2 / Platform.Windows → WPF layering. Add focused Core primitives for atomic replacement and signed update manifests, isolate concurrent path allocation inside Infrastructure.R2, and expose an awaitable upload lifecycle to WPF. Publish development history on `agent/modernize-wpf-hardening`, then squash-merge one reviewed commit into `main`.

**Tech Stack:** C# and .NET 10, WPF, MSTest, AWS SDK for S3, PowerShell 5.1+, GitHub Actions on `windows-latest`, Inno Setup 6.7.1, Wrangler 4.120.0.

## Global Constraints

- The supported application is the .NET 10 WPF client; delete the .NET Framework 4.8 WinForms frontend and compatibility test project.
- Retain `System.Windows.Forms.NotifyIcon` only as the WPF tray adapter in `Platform.Windows`.
- Preserve `%LocalAppData%\CloudflareR2Uploader`, settings migration, DPAPI migration, multipart state IDs, executable identity, and installer identity.
- Do not commit PFX files, private keys, generated manifests, installers, `bin`, `obj`, `dist`, or test results.
- Do not perform unrelated formatting, dependency upgrades, nullable migration, or UI redesign.
- All changed behavior must have a test that is observed failing before production implementation.
- Keep local commits cohesive; the final PR is squash-merged so `main` receives one implementation commit.
- Use the connected GitHub app for branch snapshots, the draft PR, workflow inspection, review, and merge because GitHub CLI is unavailable.
- Specification: `docs/superpowers/specs/2026-08-17-wpf-modernization-hardening-design.md`.

---

### Task 1: Make the .NET 10 WPF solution canonical

**Files:**
- Delete: existing legacy `CloudflareR2Uploader.sln`
- Rename: `CloudflareR2Uploader.Wpf.sln` → `CloudflareR2Uploader.sln`
- Delete: `src/CloudflareR2Uploader/**`
- Delete: `tests/CloudflareR2Uploader.Tests/CloudflareR2Uploader.Tests.csproj`
- Delete: `tests/CloudflareR2Uploader.Tests/UiConstructionTests.cs`
- Delete: `tests/CloudflareR2Uploader.Tests/Properties/AssemblyInfo.cs`
- Move: retained `tests/CloudflareR2Uploader.Tests/*.cs` files to the owning modern test project listed below
- Create: `tests/Shared/TestSupport.cs`
- Rename: `build/Modern.props` → `Directory.Build.props`
- Rename: `build/ModernTests.props` → `build/Tests.props`
- Modify: all `src/*/*.csproj` and `tests/*/*.csproj`
- Modify: `build/Build-Release.ps1`
- Delete: `docs/WPF_MIGRATION_PLAN.md`
- Delete: `docs/WPF_REDESIGN_PROGRESS.md`

**Interfaces:**
- Consumes: the current modern solution graph and linked tests.
- Produces: one canonical SDK-style solution with physical test ownership and no .NET Framework target.

- [ ] **Step 1: Install an isolated .NET 10 SDK for local Core/Infrastructure validation**

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/cloudflare-r2-uploader-dotnet-install.sh
bash /tmp/cloudflare-r2-uploader-dotnet-install.sh \
  --channel 10.0 \
  --install-dir /tmp/cloudflare-r2-uploader-dotnet \
  --no-path
export PATH="/tmp/cloudflare-r2-uploader-dotnet:$PATH"
dotnet --info
```

Expected: SDK `10.0.x` is listed. Do not write the SDK into the repository.

- [ ] **Step 2: Establish the baseline**

```bash
dotnet restore CloudflareR2Uploader.Wpf.sln -p:EnableWindowsTargeting=true
dotnet build CloudflareR2Uploader.Wpf.sln -c Release --no-restore -p:EnableWindowsTargeting=true
dotnet test tests/CloudflareR2Uploader.Core.Tests/CloudflareR2Uploader.Core.Tests.csproj -c Release --no-build
dotnet test tests/CloudflareR2Uploader.Infrastructure.Tests/CloudflareR2Uploader.Infrastructure.Tests.csproj -c Release --no-build
```

Expected: restore/build succeed and both cross-platform test projects pass. Confirm the already-observed GitHub Actions baseline at commit `bc778aaa39b9dffbfa6417f59d603632df94f91c` remains `success` for Windows-only tests and packaging.

- [ ] **Step 3: Run the structural check and observe the legacy matches**

```bash
rg -n '<TargetFrameworkVersion>|net48|SharedSourceWithLegacy|CloudflareR2Uploader\.Wpf\.sln|Migrated[\\/]' \
  --glob '*.csproj' --glob '*.sln' --glob '*.props' --glob '*.ps1'
```

Expected: FAIL the desired invariant by listing the two legacy projects, compatibility properties, linked tests, and old solution name.

- [ ] **Step 4: Move retained tests into their owning projects**

Move these files into `tests/CloudflareR2Uploader.Core.Tests/`:

```text
BrowserViewServiceTests.cs
BulkLocalPathMapperTests.cs
FileSizeFormatterTests.cs
MimeTypeServiceTests.cs
MultipartCalculatorTests.cs
ObjectKeyUtilityTests.cs
PathUtilityTests.cs
PreviewCacheServiceTests.cs
ProgressAggregationTests.cs
R2BrowserPaginationStateTests.cs
R2BrowserPathUtilityTests.cs
R2ObjectOperationPathUtilityTests.cs
SettingsServiceTests.cs
UploadStateStoreTests.cs
```

Move these files into `tests/CloudflareR2Uploader.Infrastructure.Tests/`:

```text
BulkOperationServiceTests.cs
PreviewServiceTests.cs
R2BrowserResponseMapperTests.cs
RetryServiceTests.cs
SdkContractTests.cs
TransientErrorClassifierTests.cs
UploadQueueServiceTests.cs
UrlServiceTests.cs
```

Move these files into `tests/CloudflareR2Uploader.Platform.Tests/`:

```text
CredentialProtectionTests.cs
StartupAndSingleInstanceTests.cs
UpdateServiceTests.cs
```

Move `TestSupport.cs` to `tests/Shared/TestSupport.cs`. Remove explicit linked test entries from the three modern test project files and include only the shared helper where needed:

```xml
<ItemGroup>
  <Compile Include="..\Shared\TestSupport.cs" Link="Shared\TestSupport.cs" />
</ItemGroup>
```

SDK default compile items then discover every test physically inside its project directory.

- [ ] **Step 5: Remove the compatibility projects and simplify MSBuild structure**

Delete the legacy application/test directories and the two obsolete migration-progress documents. Replace the old root solution with the modern solution under `CloudflareR2Uploader.sln`.

Move `build/Modern.props` to `Directory.Build.props`, remove the `SharedSourceWithLegacy` conditional and its compatibility-only `NoWarn` list, and rewrite the opening comment to describe shared SDK defaults. Keep `<Nullable>disable</Nullable>` globally because nullable expansion is explicitly out of scope; WPF and already-annotated files remain enabled locally.

Rename `build/ModernTests.props` to `build/Tests.props`, remove its import of `Modern.props`, and update each test project import:

```xml
<Import Project="..\..\build\Tests.props" />
```

Remove `SharedSourceWithLegacy` property groups and compatibility comments from Core, Infrastructure.R2, Platform.Windows, and their test projects. Keep `UseWindowsForms` only in Platform.Windows/WPF for `NotifyIcon`.

Update `build/Build-Release.ps1`:

```powershell
$solutionPath = Join-Path $repositoryRoot 'CloudflareR2Uploader.sln'
```

- [ ] **Step 6: Verify the canonical structure**

```bash
test ! -e src/CloudflareR2Uploader
test ! -e tests/CloudflareR2Uploader.Tests
test ! -e CloudflareR2Uploader.Wpf.sln
test -f CloudflareR2Uploader.sln
! rg -n '<TargetFrameworkVersion>|net48|SharedSourceWithLegacy|CloudflareR2Uploader\.Wpf\.sln|Migrated[\\/]' \
  --glob '*.csproj' --glob '*.sln' --glob '*.props' --glob '*.ps1'
dotnet restore CloudflareR2Uploader.sln -p:EnableWindowsTargeting=true
dotnet build CloudflareR2Uploader.sln -c Release --no-restore -p:EnableWindowsTargeting=true
dotnet test tests/CloudflareR2Uploader.Core.Tests/CloudflareR2Uploader.Core.Tests.csproj -c Release --no-build
dotnet test tests/CloudflareR2Uploader.Infrastructure.Tests/CloudflareR2Uploader.Infrastructure.Tests.csproj -c Release --no-build
```

Expected: no structural matches, build succeeds, and cross-platform tests pass.

- [ ] **Step 7: Commit the repository consolidation**

```bash
git add CloudflareR2Uploader.sln Directory.Build.props build src tests docs
git diff --cached --check
git commit -m "Retire the legacy WinForms architecture"
```

---

### Task 2: Make persistence atomic and history clearing truthful

**Files:**
- Create: `src/CloudflareR2Uploader.Core/Services/AtomicFileWriter.cs`
- Create: `tests/CloudflareR2Uploader.Core.Tests/AtomicFileWriterTests.cs`
- Modify: `src/CloudflareR2Uploader.Core/Services/SettingsService.cs`
- Modify: `src/CloudflareR2Uploader.Core/Services/UploadStateStore.cs`
- Modify: `src/CloudflareR2Uploader.Core/Services/ActivityHistoryService.cs`
- Modify: `src/CloudflareR2Uploader.Core/Services/IActivityHistoryService.cs`
- Modify: `src/CloudflareR2Uploader.Platform.Windows/Services/CredentialProtectionService.cs`
- Modify: `tests/CloudflareR2Uploader.Core.Tests/SettingsServiceTests.cs`
- Modify: `tests/CloudflareR2Uploader.Core.Tests/UploadStateStoreTests.cs`
- Modify: `tests/CloudflareR2Uploader.Core.Tests/ActivityHistoryServiceTests.cs`
- Modify: `tests/CloudflareR2Uploader.Platform.Tests/CredentialProtectionTests.cs`
- Modify: `src/CloudflareR2Uploader.Wpf/ViewModels/Activity/ActivityViewModel.cs`
- Modify: WPF fakes implementing `IActivityHistoryService`

**Interfaces:**
- Produces: `IAtomicFileWriter.Write(string, Action<FileStream>)` and `Task<bool> IActivityHistoryService.ClearAsync(...)`.
- Consumes: existing caller serialization and logging behavior.

- [ ] **Step 1: Write failing atomic-replacement tests**

Create a test committer that throws after the temporary file has been flushed:

```csharp
private sealed class ThrowingCommitter : IAtomicFileCommitter
{
    public void Commit(string temporaryPath, string destinationPath) =>
        throw new IOException("synthetic replacement failure");
}

[TestMethod]
public void Write_WhenCommitFails_PreservesDestinationAndCleansTemporaryFile()
{
    using TemporaryDirectory directory = new();
    string destination = directory.File("settings.json");
    File.WriteAllText(destination, "old");
    AtomicFileWriter writer = new(new ThrowingCommitter());

    Assert.ThrowsException<IOException>(() =>
        writer.Write(destination, stream =>
        {
            byte[] bytes = Encoding.UTF8.GetBytes("new");
            stream.Write(bytes, 0, bytes.Length);
        }));

    Assert.AreEqual("old", File.ReadAllText(destination));
    Assert.AreEqual(0, Directory.GetFiles(directory.Path, "*.tmp").Length);
}
```

Add integration tests using an injected `AtomicFileWriter(new ThrowingCommitter())` to prove `SettingsService.Save`, `UploadStateStore.Save`, credential save, and activity rewrite return their documented failure result while retaining the old destination bytes.

- [ ] **Step 2: Write the failing activity-clear tests**

Extend `ActivityHistoryServiceTests`:

```csharp
[TestMethod]
public async Task Clear_WhenDeleteFails_ReturnsFalseAndDoesNotRaiseCleared()
{
    using TemporaryDirectory directory = new();
    string path = directory.File("activity.jsonl");
    using ActivityHistoryService service = new(null, path);
    await service.RecordAsync(Record(ActivityAction.Upload));
    bool raised = false;
    service.Cleared += (_, _) => raised = true;

    using FileStream locked = new(path, FileMode.Open, FileAccess.Read, FileShare.None);
    bool result = await service.ClearAsync();

    Assert.IsFalse(result);
    Assert.IsFalse(raised);
    Assert.IsTrue(File.Exists(path));
}
```

Update the existing success test to assert `Assert.IsTrue(await service.ClearAsync())`. Add a WPF view-model test with a fake history returning `false`; assert no success toast and one error surface.

- [ ] **Step 3: Run the tests and verify RED**

```bash
dotnet test tests/CloudflareR2Uploader.Core.Tests/CloudflareR2Uploader.Core.Tests.csproj \
  -c Release --filter 'AtomicFileWriterTests|ActivityHistoryServiceTests'
```

Expected: compile/test failure because atomic writer contracts and boolean `ClearAsync` do not exist.

- [ ] **Step 4: Implement one atomic writer**

Create the focused contracts:

```csharp
internal interface IAtomicFileWriter
{
    void Write(string destinationPath, Action<FileStream> write);
}

internal interface IAtomicFileCommitter
{
    void Commit(string temporaryPath, string destinationPath);
}

internal sealed class AtomicFileWriter : IAtomicFileWriter
{
    internal static AtomicFileWriter Shared { get; } = new(new FileSystemCommitter());

    public void Write(string destinationPath, Action<FileStream> write)
    {
        string directory = Path.GetDirectoryName(destinationPath)!;
        string temporary = Path.Combine(directory,
            "." + Path.GetFileName(destinationPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(stream);
                stream.Flush(true);
            }
            _committer.Commit(temporary, destinationPath);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
```

`FileSystemCommitter.Commit` uses `File.Replace(temporary, destination, null, true)` when the destination exists and `File.Move` when absent. It never deletes the destination first.

Add internal constructor overloads to the four services so tests can inject `IAtomicFileWriter`; public constructors delegate to `AtomicFileWriter.Shared`. Replace every `.tmp` + delete + move sequence with `_atomicFiles.Write(...)`. Ensure serializer/writer wrappers leave the provided stream open until `AtomicFileWriter` performs `Flush(true)`.

- [ ] **Step 5: Implement truthful clear behavior and UI handling**

Change the interface and service:

```csharp
Task<bool> ClearAsync(CancellationToken cancellationToken = default);
```

Return `true` and raise `Cleared` only after `!File.Exists(_filePath)`. On recoverable failure, log and return `false` without raising the event.

In `ActivityViewModel.ClearHistoryAsync`:

```csharp
bool cleared = await _history.ClearAsync().ConfigureAwait(true);
await ReloadAsync().ConfigureAwait(true);
if (cleared) _toasts.ShowSuccess("Activity history cleared");
else await _dialogs.ShowErrorAsync(
    "Activity history could not be cleared",
    "The history file is still present. Close programs using it and try again.")
    .ConfigureAwait(true);
```

Update all fakes to return `Task.FromResult(true)` or their configured result.

- [ ] **Step 6: Verify GREEN and commit**

```bash
dotnet test tests/CloudflareR2Uploader.Core.Tests/CloudflareR2Uploader.Core.Tests.csproj -c Release
dotnet build CloudflareR2Uploader.sln -c Release -p:EnableWindowsTargeting=true
git add src tests
git diff --cached --check
git commit -m "Make local persistence crash-safe"
```

Expected: Core tests pass and the Windows projects compile.

---

### Task 3: Enforce upload identity and reserve bulk-download paths

**Files:**
- Create: `src/CloudflareR2Uploader.Infrastructure.R2/Services/UploadObjectVerifier.cs`
- Create: `src/CloudflareR2Uploader.Infrastructure.R2/Services/BulkDownloadPathAllocator.cs`
- Create: `tests/CloudflareR2Uploader.Infrastructure.Tests/UploadObjectVerifierTests.cs`
- Modify: `src/CloudflareR2Uploader.Infrastructure.R2/Services/R2UploadService.cs`
- Modify: `src/CloudflareR2Uploader.Infrastructure.R2/Services/R2BulkOperationService.cs`
- Modify: `tests/CloudflareR2Uploader.Infrastructure.Tests/BulkOperationServiceTests.cs`

**Interfaces:**
- Produces: `UploadObjectVerifier.Verify(...)` and `BulkDownloadPathAllocator.AllocateRename(string)`.
- Consumes: `AppSettings.VerifyETagAfterUpload`, upload/HEAD ETags, and planned local paths.

- [ ] **Step 1: Write failing verifier tests**

Cover quoted/case-insensitive equality, mismatch, missing upload ETag, missing HEAD ETag, size mismatch, and disabled ETag verification. The central mismatch case is:

```csharp
[TestMethod]
public void Verify_SameSizeButDifferentETag_FailsWhenEnabled()
{
    UploadQueueItem item = new("C:\\input.bin", "input.bin", 4, DateTime.UtcNow);
    AppSettings settings = new() { VerifyETagAfterUpload = true };
    UploadObjectVerifier verifier = new(null);

    UploadResult result = verifier.Verify(
        item, settings, "input.bin", remoteLength: 4,
        uploadETag: "\"aaaa\"", headETag: "\"bbbb\"", wasMultipart: false);

    Assert.AreEqual(UploadOutcome.Failed, result.Outcome);
    Assert.AreEqual(UploadItemStatus.Failed, item.Status);
}
```

The disabled test uses the same mismatch and expects success while retaining mandatory size validation.

- [ ] **Step 2: Write the failing parallel rename regression**

Construct a plan whose original paths are `report.txt` and `report (1).txt`, pre-create `report.txt`, and use a fake source with a two-party barrier so both downloads overlap. Run with `RenameAutomatically`; assert two successes, distinct final contents, and no `.r2partial` files.

```csharp
Assert.AreEqual(2, result.Succeeded);
Assert.AreEqual("second", File.ReadAllText(Path.Combine(root, "report (1).txt")));
Assert.AreEqual("first", File.ReadAllText(Path.Combine(root, "report (2).txt")));
Assert.AreEqual(0, Directory.GetFiles(root, "*.r2partial").Length);
```

- [ ] **Step 3: Run and verify RED**

```bash
dotnet test tests/CloudflareR2Uploader.Infrastructure.Tests/CloudflareR2Uploader.Infrastructure.Tests.csproj \
  -c Release --filter 'UploadObjectVerifierTests|BulkOperationServiceTests'
```

Expected: verifier type missing and the parallel rename test fails or records a collision.

- [ ] **Step 4: Extract and use `UploadObjectVerifier`**

Move size/ETag result construction out of `R2UploadService.VerifyAsync` into the new class. Normalize ETags as follows:

```csharp
internal static string NormalizeETag(string value)
{
    string normalized = (value ?? string.Empty).Trim();
    if (normalized.Length >= 2 && normalized[0] == '"' && normalized[^1] == '"')
        normalized = normalized[1..^1];
    return normalized;
}
```

When verification is enabled, fail if either normalized value is empty or if `!string.Equals(upload, head, StringComparison.OrdinalIgnoreCase)`. Always fail a size mismatch. `R2UploadService.VerifyAsync` performs `HEAD` once and passes response metadata to the verifier.

- [ ] **Step 5: Allocate names under one reservation owner**

Create `BulkDownloadPathAllocator` with `StringComparer.OrdinalIgnoreCase`. Its constructor registers every planned final path and each `path + ".r2partial"`. `AllocateRename` runs under a private lock and accepts a candidate only when both candidate names are absent from `_reserved` and the filesystem. It reserves both before returning.

Create the allocator before workers start:

```csharp
BulkDownloadPathAllocator paths = new(
    plan.Objects.Where(entry => !entry.IsFolderMarker).Select(entry => entry.LocalPath),
    value => File.Exists(PathUtility.ToExtendedLengthPath(value)));
```

Replace the direct `FindAvailable` call with `paths.AllocateRename(target)`. Original targets remain reserved for their owning plan entries; renamed targets cannot collide with another original or partial path.

- [ ] **Step 6: Verify GREEN and commit**

```bash
dotnet test tests/CloudflareR2Uploader.Infrastructure.Tests/CloudflareR2Uploader.Infrastructure.Tests.csproj -c Release
git add src/CloudflareR2Uploader.Infrastructure.R2 tests/CloudflareR2Uploader.Infrastructure.Tests
git diff --cached --check
git commit -m "Harden upload and download verification"
```

---

### Task 4: Await cancellation cleanup before exit and fix update progress text

**Files:**
- Create: `src/CloudflareR2Uploader.Infrastructure.R2/Services/IUploadQueueLifecycle.cs`
- Create: `tests/CloudflareR2Uploader.Wpf.Tests/ApplicationExitCoordinatorTests.cs`
- Modify: `src/CloudflareR2Uploader.Infrastructure.R2/Services/UploadQueueService.cs`
- Modify: `tests/CloudflareR2Uploader.Infrastructure.Tests/UploadQueueServiceTests.cs`
- Modify: `src/CloudflareR2Uploader.Wpf/Services/ApplicationExitCoordinator.cs`
- Modify: `src/CloudflareR2Uploader.Wpf/App.xaml.cs`
- Modify: `src/CloudflareR2Uploader.Wpf/ViewModels/SettingsViewModel.cs`
- Modify: `tests/CloudflareR2Uploader.Wpf.Tests/ViewModelBehaviorTests.cs`

**Interfaces:**
- Produces: `IUploadQueueLifecycle.StopAsync(bool, CancellationToken)`.
- Consumes: the existing queue singleton, keep/discard dialog, exit/update callers, and progress model.

- [ ] **Step 1: Write the failing queue-stop test**

Add an internal queue constructor accepting `Func<CancellationToken, Task>? runOverride` for deterministic lifecycle tests. The test runner catches cancellation, signals that cleanup started, then waits on a release source:

```csharp
Task stop = queue.StopAsync(preserveParts: true, CancellationToken.None);
await cleanupStarted.Task;
Assert.IsFalse(stop.IsCompleted, "StopAsync must wait for cancellation cleanup.");
cleanupFinished.SetResult();
await stop;
```

Assert the override observed cancellation and every item received `PreservePartsOnCancel = true`.

- [ ] **Step 2: Write failing WPF coordinator and formatter tests**

Use a fake `IUploadQueueLifecycle` whose `StopAsync` is controlled by a `TaskCompletionSource`. Assert `PrepareAsync` remains incomplete until stop completes. Inject a 10 ms timeout in a second test and assert `PrepareAsync` returns `false` and `ShowErrorAsync` is called.

Add direct formatter assertions:

```csharp
Assert.AreEqual("Downloading update… 50%",
    SettingsViewModel.FormatUpdateProgress(new UpdateDownloadProgress
    {
        BytesReceived = 50,
        TotalBytes = 100,
        Percentage = 50
    }));
Assert.IsFalse(SettingsViewModel.FormatUpdateProgress(new()).Contains("â"));
```

- [ ] **Step 3: Verify RED locally and on Windows**

```bash
dotnet test tests/CloudflareR2Uploader.Infrastructure.Tests/CloudflareR2Uploader.Infrastructure.Tests.csproj \
  -c Release --filter UploadQueueServiceTests
dotnet build CloudflareR2Uploader.sln -c Release -p:EnableWindowsTargeting=true
```

Publish the test-only snapshot to the feature branch and inspect the draft PR's Windows job. Expected: the new WPF tests fail because `PrepareAsync` returns before lifecycle completion and the text contains mojibake.

- [ ] **Step 4: Implement the lifecycle contract**

```csharp
public interface IUploadQueueLifecycle
{
    bool IsRunning { get; }
    IReadOnlyList<UploadQueueItem> GetItems();
    Task StopAsync(bool preserveParts, CancellationToken cancellationToken);
}
```

`StopAsync` sets preservation state, calls existing cancellation signaling, captures `_runTask` under `_sync`, and awaits `runTask.WaitAsync(cancellationToken)` when non-null. `Dispose` must not dispose `_runCancellation` or `_pauseController` while a captured run task is active.

Register one singleton under both types:

```csharp
services.AddSingleton<UploadQueueService>();
services.AddSingleton<IUploadQueueLifecycle>(provider =>
    provider.GetRequiredService<UploadQueueService>());
```

- [ ] **Step 5: Await cleanup in the exit coordinator**

Inject `IUploadQueueLifecycle`, `IDialogService`, `ILoggingService`, and an internal timeout defaulting to 45 seconds. After the user's keep/discard choice:

```csharp
using CancellationTokenSource timeout = new(_stopTimeout);
try
{
    await _queue.StopAsync(keepParts.Value, timeout.Token).ConfigureAwait(true);
    return true;
}
catch (Exception ex) when (ex is OperationCanceledException or IOException)
{
    _log.Error("App.Exit", "Upload cleanup did not complete before exit.", ex);
    await _dialogs.ShowErrorAsync(
        "The app is still finishing an upload",
        "Cloudflare R2 Uploader stayed open so resumable upload state is not lost.")
        .ConfigureAwait(true);
    return false;
}
```

Tray exit and installer launch already call `PrepareAsync`; keep that ordering. Route any remaining real window-close path through the coordinator or prevent it from bypassing the tray-controlled exit.

- [ ] **Step 6: Extract the progress formatter and verify GREEN**

Replace both malformed literals with `FormatUpdateProgress`, returning `Downloading update… {Percentage}%` when total bytes are known and `Downloading update… {formatted bytes}` otherwise.

```bash
dotnet test tests/CloudflareR2Uploader.Infrastructure.Tests/CloudflareR2Uploader.Infrastructure.Tests.csproj -c Release
dotnet build CloudflareR2Uploader.sln -c Release -p:EnableWindowsTargeting=true
```

Publish the implementation snapshot and require the Windows WPF/Platform tests to pass.

- [ ] **Step 7: Commit**

```bash
git add src tests
git diff --cached --check
git commit -m "Wait for safe upload shutdown"
```

---

### Task 5: Authenticate the update channel and add the signing tool

**Files:**
- Move/replace: `src/CloudflareR2Uploader.Platform.Windows/Models/UpdateModels.cs` → `src/CloudflareR2Uploader.Core/Models/UpdateModels.cs`
- Create: `src/CloudflareR2Uploader.Core/Services/UpdateManifestSignature.cs`
- Create: `tests/CloudflareR2Uploader.Core.Tests/UpdateManifestSignatureTests.cs`
- Modify: `src/CloudflareR2Uploader.Platform.Windows/Services/UpdateService.cs`
- Modify: `tests/CloudflareR2Uploader.Platform.Tests/UpdateServiceTests.cs`
- Modify: `src/CloudflareR2Uploader.Platform.Windows/CloudflareR2Uploader.Platform.Windows.csproj`
- Create: `tools/CloudflareR2Uploader.UpdateSigner/CloudflareR2Uploader.UpdateSigner.csproj`
- Create: `tools/CloudflareR2Uploader.UpdateSigner/Program.cs`
- Modify: `CloudflareR2Uploader.sln`
- Modify: `build/Build-Release.ps1`
- Modify: `deploy/Publish-CloudflareUpdate.ps1`
- Modify: WPF update call sites in `TrayIconService.cs` and `SettingsViewModel.cs`

**Interfaces:**
- Produces: signed schema-1 `UpdateManifest`, `UpdateManifestSignature.Sign/Verify`, and typed `UpdateSourceConfiguration` containing URL plus trusted SubjectPublicKeyInfo.
- Consumes: RSA private key from a password-protected PFX only in the signer tool; runtime receives public key only.

- [ ] **Step 1: Write failing cross-platform signature tests**

Generate ephemeral keys in tests:

```csharp
using RSA trusted = RSA.Create(2048);
using RSA attacker = RSA.Create(2048);
UpdateManifest manifest = Manifest("2.0.0");
UpdateManifestSignature.Sign(manifest, trusted);

Assert.IsTrue(UpdateManifestSignature.Verify(
    manifest, Convert.ToBase64String(trusted.ExportSubjectPublicKeyInfo())));
manifest.Notes = "altered";
Assert.IsFalse(UpdateManifestSignature.Verify(
    manifest, Convert.ToBase64String(trusted.ExportSubjectPublicKeyInfo())));
Assert.IsFalse(UpdateManifestSignature.Verify(
    Manifest("2.0.0"), Convert.ToBase64String(attacker.ExportSubjectPublicKeyInfo())));
```

Also cover missing signature, unsupported algorithm, malformed base64, UTC timestamp normalization, Unicode notes, and a serialize/deserialize round trip using `System.Text.Json`.

- [ ] **Step 2: Write failing runtime update tests**

Change tests to load a complete `update-source.json` and call `CheckAsync(source, currentVersion, token)`. Add cases for unsigned, altered, and wrong-key manifests. `DownloadInstallerAsync` must receive the same source and reverify the manifest immediately before downloading.

- [ ] **Step 3: Verify RED**

```bash
dotnet test tests/CloudflareR2Uploader.Core.Tests/CloudflareR2Uploader.Core.Tests.csproj \
  -c Release --filter UpdateManifestSignatureTests
dotnet build CloudflareR2Uploader.sln -c Release -p:EnableWindowsTargeting=true
```

Expected: signature contracts are missing. Publish the test-only snapshot and confirm the Windows Platform tests fail for unsigned acceptance.

- [ ] **Step 4: Implement canonical signing in Core**

Use `System.Text.Json.Serialization` attributes on update models and remove the Platform project's sole `Newtonsoft.Json` dependency. Add:

```csharp
public const string Algorithm = "RSA-PSS-SHA256";

public static void Sign(UpdateManifest manifest, RSA privateKey)
{
    ValidateShape(manifest);
    manifest.SignatureAlgorithm = Algorithm;
    manifest.Signature = Convert.ToBase64String(
        privateKey.SignData(CreatePayload(manifest), HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss));
}
```

`CreatePayload` writes magic `CloudflareR2Uploader.UpdateManifest.v1`, schema, version, URL, lowercase hash, size, UTC ticks, and notes with `BinaryWriter` in a fixed order. `Verify` validates shape/algorithm/base64, imports the SubjectPublicKeyInfo with `RSA.ImportSubjectPublicKeyInfo`, and uses `VerifyData` with PSS.

`UpdateSourceConfiguration` contains:

```csharp
public string ManifestUrl { get; set; } = string.Empty;
public string ManifestPublicKey { get; set; } = string.Empty;
```

- [ ] **Step 5: Require verification in `UpdateService`**

Replace `LoadManifestUrl` with `LoadUpdateSource`. It returns null unless the URL is absolute HTTPS and the public key decodes/imports successfully. Change APIs:

```csharp
Task<UpdateCheckResult> CheckAsync(
    UpdateSourceConfiguration source, string currentVersion, CancellationToken cancellationToken);
Task<string> DownloadInstallerAsync(
    UpdateSourceConfiguration source, UpdateManifest manifest,
    IProgress<UpdateDownloadProgress>? progress, CancellationToken cancellationToken);
```

Verify the signature after deserialization and again before installer download. Preserve bounded reads, HTTPS redirect checks, size validation, fixed-time SHA-256 comparison, and partial-file cleanup. Update WPF fields/call sites to retain the trusted source with the available manifest.

- [ ] **Step 6: Implement the .NET 10 signer CLI**

The tool references Core only and supports:

```text
export-public-key --pfx PATH --password-env NAME
sign --pfx PATH --password-env NAME --version VERSION --installer PATH
     --installer-url HTTPS_URL --notes TEXT --output PATH
```

Read the password with `Environment.GetEnvironmentVariable`; reject missing/empty values. Load the PFX with `X509CertificateLoader.LoadPkcs12FromFile`, obtain RSA via `GetRSAPrivateKey`, construct the manifest, hash the installer, call `UpdateManifestSignature.Sign`, and write UTF-8 JSON without BOM. Never print the password, private parameters, or PFX bytes.

- [ ] **Step 7: Wire secure build and publishing configuration**

Add `UpdateManifestPublicKey` to `Build-Release.ps1`. Reject incomplete pairs:

```powershell
$hasUpdateUrl = -not [string]::IsNullOrWhiteSpace($UpdateBaseUrl)
$hasUpdateKey = -not [string]::IsNullOrWhiteSpace($UpdateManifestPublicKey)
if ($hasUpdateUrl -ne $hasUpdateKey) {
    throw 'UpdateBaseUrl and UpdateManifestPublicKey must be supplied together.'
}
```

Write both values to `update-source.json`. CI builds without production update variables; tagged release passes both `UPDATE_BASE_URL` and `UPDATE_MANIFEST_PUBLIC_KEY` repository variables.

Change the publisher to require `-SigningCertificatePath` and a password environment variable name, call the signer tool, then use `npx --yes wrangler@4.120.0`. Its post-upload check downloads the manifest and invokes the same verifier through the signer/tooling path.

- [ ] **Step 8: Verify GREEN and commit**

```bash
dotnet test tests/CloudflareR2Uploader.Core.Tests/CloudflareR2Uploader.Core.Tests.csproj -c Release
dotnet build CloudflareR2Uploader.sln -c Release -p:EnableWindowsTargeting=true
```

Publish the implementation snapshot and require all Windows Platform/WPF tests and the package step to pass.

```bash
git add CloudflareR2Uploader.sln src tests tools build deploy
git diff --cached --check
git commit -m "Authenticate the update channel"
```

---

### Task 6: License, pin, document, and validate the supported repository

**Files:**
- Create: `LICENSE`
- Create: `.github/dependabot.yml`
- Modify: `.github/workflows/ci.yml`
- Modify: `.github/workflows/release.yml`
- Modify: `.gitignore`
- Modify: `README.md`
- Modify: `CONTRIBUTING.md`
- Modify: `SECURITY.md`
- Modify: `THIRD-PARTY-NOTICES.md`
- Modify: `docs/RELEASING.md`
- Modify: `deploy/README.md`
- Modify: remaining comments referring to a live WinForms frontend

**Interfaces:**
- Produces: MIT-licensed WPF-only repository, reproducible workflows, and an operator-ready signing guide.
- Consumes: the final project graph and signer CLI from Tasks 1–5.

- [ ] **Step 1: Add the approved MIT license**

Use the standard MIT text beginning:

```text
MIT License

Copyright (c) 2026 Saboreq
```

Update README license status and allow the existing release script to copy `LICENSE` into the installer payload.

- [ ] **Step 2: Pin GitHub Actions and build tools**

Use these verified action commits with version comments in both workflows:

```yaml
- uses: actions/checkout@d23441a48e516b6c34aea4fa41551a30e30af803 # v6
- uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5
- uses: actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a # v7
```

Install the reviewed Chocolatey package exactly:

```yaml
run: choco install innosetup --version=6.7.1 --no-progress -y
```

Keep Wrangler at `4.120.0` in publisher commands and examples.

- [ ] **Step 3: Add bounded Dependabot configuration**

```yaml
version: 2
updates:
  - package-ecosystem: nuget
    directory: /
    schedule:
      interval: weekly
      day: monday
      time: "06:00"
      timezone: Europe/Warsaw
    open-pull-requests-limit: 5
    groups:
      nuget-minor-and-patch:
        update-types: [minor, patch]
  - package-ecosystem: github-actions
    directory: /
    schedule:
      interval: weekly
      day: monday
      time: "06:00"
      timezone: Europe/Warsaw
    open-pull-requests-limit: 3
```

- [ ] **Step 4: Rewrite operator and contributor documentation**

Document only `CloudflareR2Uploader.sln`, .NET 10, WPF, the four-layer source tree, modern test projects, and `Build-Release.ps1`. Remove requirements for .NET Framework 4.8, C# 7.3, linked files, compatibility builds, Ookii, Fody, and Costura.

The release/update guide must include:

1. owner-generated password-protected PFX kept outside the repo;
2. `UPDATE_SIGNING_CERTIFICATE_PASSWORD` set only in the publishing shell;
3. `export-public-key` output stored as repository variable `UPDATE_MANIFEST_PUBLIC_KEY`;
4. release builds requiring URL plus public key;
5. publisher command using the PFX path and password environment name;
6. key rotation requiring a client release that trusts the new key before switching signatures;
7. no claim that manifest signing replaces Authenticode.

Add `deploy/private/`, `*.pfx`, signed manifest output, and signer scratch output to `.gitignore` without ignoring public `.cer` documentation samples globally.

- [ ] **Step 5: Run static repository checks**

```bash
git diff --check
! rg -n '<TargetFrameworkVersion>|net48|SharedSourceWithLegacy|CloudflareR2Uploader\.Wpf\.sln|Migrated[\\/]' \
  --glob '*.csproj' --glob '*.sln' --glob '*.props' --glob '*.ps1'
! rg -n 'wrangler@latest|actions/(checkout|setup-dotnet|upload-artifact)@v[0-9]+' .github deploy
! rg -n 'â€¦' src tests docs README.md
test -f LICENSE
test -f .github/dependabot.yml
git status --short
```

Expected: no prohibited matches; only intended tracked changes are present.

- [ ] **Step 6: Commit the repository hardening**

```bash
git add .github .gitignore LICENSE README.md CONTRIBUTING.md SECURITY.md \
  THIRD-PARTY-NOTICES.md docs deploy src tests
git diff --cached --check
git commit -m "Harden release and repository policy"
```

---

### Task 7: Full verification, independent review, PR, and main integration

**Files:**
- Verify: the complete branch diff from `bc778aaa39b9dffbfa6417f59d603632df94f91c` to feature HEAD
- Modify only files required by validated review feedback
- GitHub: draft PR → ready PR → squash merge into `main`

**Interfaces:**
- Consumes: every acceptance criterion in the approved specification.
- Produces: one green, reviewed commit on `main` and an explicit list of any repository setting that still requires owner action.

- [ ] **Step 1: Run fresh local verification**

```bash
export PATH="/tmp/cloudflare-r2-uploader-dotnet:$PATH"
git diff --check bc778aaa39b9dffbfa6417f59d603632df94f91c..HEAD
dotnet restore CloudflareR2Uploader.sln -p:EnableWindowsTargeting=true
dotnet build CloudflareR2Uploader.sln -c Release --no-restore -p:EnableWindowsTargeting=true
dotnet test tests/CloudflareR2Uploader.Core.Tests/CloudflareR2Uploader.Core.Tests.csproj -c Release --no-build
dotnet test tests/CloudflareR2Uploader.Infrastructure.Tests/CloudflareR2Uploader.Infrastructure.Tests.csproj -c Release --no-build
git status -sb
```

Expected: clean diff check, build success, all runnable tests pass, and no uncommitted files.

- [ ] **Step 2: Publish final branch snapshot and require the full Windows workflow**

Use the GitHub app to fast-forward `agent/modernize-wpf-hardening`, update the draft PR, and inspect every step of `build-test-package`:

```text
checkout
setup-dotnet
install pinned Inno Setup
restore/build/test/package
upload installer artifact
upload TRX test results
```

Expected: one completed job with conclusion `success`. Download/inspect the TRX artifact and report total tests and zero failures. Confirm the installer artifact contains exactly one setup EXE and no PFX/private-key file.

- [ ] **Step 3: Dispatch an independent read-only review**

Use `superpowers:requesting-code-review` with:

```text
DESCRIPTION: Removed the legacy net48 WinForms architecture; fixed updater authentication,
awaited shutdown, atomic persistence, ETag verification, bulk path allocation, activity
clearing, UI encoding, licensing, and supply-chain configuration.
PLAN_OR_REQUIREMENTS: docs/superpowers/specs/2026-08-17-wpf-modernization-hardening-design.md
BASE_SHA: bc778aaa39b9dffbfa6417f59d603632df94f91c
HEAD_SHA: run `git rev-parse HEAD` immediately before dispatch and paste that exact SHA
```

Fix every Critical and Important issue with a regression test where behavioral, rerun local checks, republish, and require Windows CI success again. Record reasoned responses to any rejected false positive.

- [ ] **Step 4: Re-read requirements and verify coverage**

Check each acceptance criterion in the specification against a file, test, or workflow result. Run a targeted secret scan:

```bash
! rg -n --hidden --glob '!**/.git/**' --glob '!**/bin/**' --glob '!**/obj/**' \
  -- '-----BEGIN .*PRIVATE KEY-----|AKIA[0-9A-Z]{16}|secretAccessKey\s*[:=]' .
! rg --files --hidden -g '!**/.git/**' -g '!**/bin/**' -g '!**/obj/**' | \
  rg -i -- '\.pfx$'
```

Expected: no production credential or signing key. Synthetic test credentials must remain visibly synthetic and bounded to tests.

- [ ] **Step 5: Mark the PR ready and squash-merge into `main`**

Use title:

```text
Modernize the WPF architecture and harden transfers
```

The PR body must summarize removals, each root-cause fix, compatibility behavior, signing-key owner setup, tests, Windows package result, and independent review. Merge with method `squash` only after the expected feature HEAD SHA and successful workflow are rechecked.

- [ ] **Step 6: Verify the merged `main` commit**

Fetch the resulting `main` SHA through GitHub, confirm the PR is merged, and inspect the push-triggered CI job on that exact SHA. Expected: the complete Windows build/test/package job succeeds again.

- [ ] **Step 7: Apply or report GitHub security settings**

After merge, use the available GitHub administration interface to:

- protect `main`;
- require the `build-test-package` check;
- require pull-request review before merge;
- block force pushes and branch deletion;
- enable Dependabot alerts/security updates;
- enable automatic feature-branch deletion after merge.

Read the settings back. If a setting is unavailable through the connector/browser, stop at the permission boundary and report the exact owner click-path; do not claim it was enabled.
