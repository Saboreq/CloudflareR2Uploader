# Final whole-branch fix report

Date: 2026-08-21
Starting commit: `e380481ecfc6367cf512af29ec6df2702e3f02e6`

## Findings closed

1. **Owned download disposal is one-shot.** A thread-safe `Interlocked.Exchange` gate now makes published and unpublished disposal idempotent, including cleanup failure retention. RED: the new repeated/concurrent regressions observed repeated native abandon and stream-dispose calls (up to four calls instead of one). GREEN: all three focused owned-download disposal tests passed.
2. **Deferred queue disposal has a real completion barrier.** The existing `_disposalCompleted` lifecycle signal is exposed internally as `DisposalCompleted`; the regression awaits it before probing both cancellation and pause resources. RED: the test initially failed to compile with `CS1061` because no completion contract was exposed. GREEN: the focused disposal lifecycle tests passed and both resources were disposed after the barrier.
3. **Authenticated R2 reads are bounded while materializing.** Wrangler retrieval now uses `--pipe`, a five-minute deadline enforced by bounded waits on the owning PowerShell runspace, an exact independent byte ceiling, a non-shared destination stream, and failure cleanup. Installer preflight, installer read-back, and manifest read-back use the expected immutable byte length. RED: the policy regression found no piped process, byte cap, or timeout, and the prior `--file` path delegated materialization to Wrangler. GREEN: signer/release policy tests passed; the PowerShell harness now covers oversized preflight and both read-backs, partial failure, absent or dishonest length metadata, overflow during streaming, and a hanging Wrangler descendant.
4. **Release notes use 8,000 Unicode scalar values everywhere.** Core and publisher count valid surrogate pairs as one scalar and reject malformed pairs; signer and schema coverage exercises 8,000/8,001 astral scalars. RED: Core rejected the valid 8,000-astral case because it counted 16,000 UTF-16 code units. GREEN: Core, JSON, signer CLI, schema, and publisher policy regressions passed.
5. **Workflow policy fails closed on hidden YAML keys.** Escaped double-quoted mapping keys and explicit mapping indicators are rejected, including forms that conceal governed `uses` or `run` keys. RED: an escaped `"\\u0075ses"` mutation was accepted. GREEN: escaped `uses`/`run` and explicit-mapping mutations are rejected.
6. **PowerShell governed tokens normalize bareword backticks.** The policy tokenizer consumes a backtick escape outside quotes and fails closed on a dangling escape. RED: mutations hiding `choco` and `innosetup` behind backticks were accepted. GREEN: both executable and package-token mutations are rejected.
7. **Exit cleanup handles nonfatal stop failures at the correct boundary.** Only `StopAsync` is wrapped; nonfatal failures log and show the existing safe-open error, while keep-parts dialog failures still propagate. The `InvalidOperationException` and dialog-boundary regressions were authored before production changes. Runtime RED/GREEN execution is Windows CI-gated because this Linux host has no `Microsoft.WindowsDesktop.App`; the complete WPF test project compiles with warnings as errors.
8. **Installer launch closes the verification race.** Immediately after shutdown preparation and before launch, the app snapshots and verifies the source/manifest trust binding, opens the exact installer while denying write/delete sharing, revalidates signed length and SHA-256 from that held handle, and keeps it open through `Process.Start`. Failures release exit preparation and leave the app open. RED: the new caller regressions initially failed to compile because the verified-launch contract did not exist. GREEN: platform and WPF regression projects compile, covering same-length substitution after confirmation, substitution during shutdown preparation, wrong trust root, held-file replacement denial, launcher failure recovery, and success/commit. Runtime execution is Windows CI-gated on this Linux host.

## Final verification

- Direct .NET 10.0.400 Release solution rebuild, serialized with Windows targeting and warnings as errors: **10/10 projects built; 0 warnings; 0 errors**.
- Runnable Core tests: **123 passed, 0 failed, 0 skipped**.
- Runnable Infrastructure tests: **115 passed, 0 failed, 0 skipped**.
- Runnable signer/repository-policy tests: **56 passed, 0 failed, 0 skipped**.
- Platform and WPF test assemblies built successfully; direct execution aborted only because the Linux SDK installation has no Windows Desktop runtime. These suites remain required in Windows CI.
- Neither `pwsh` nor Windows PowerShell is installed locally. PowerShell behavioral harness execution remains required in Windows CI; its static release-policy coverage passed locally.
- `git diff --check` passed. Tracked artifact/key scans, private-key marker scan, suspicious credential assignment scan, WPF transient scan, and live stale-architecture scan were clean. The only legacy phrases found were deny-list literals inside the repository-policy tests themselves.

## Compatibility and scope

No installer re-download, manifest trust weakening, update-consent change, settings-format change, upload-state change, or public release-verification reduction was introduced. The change set contains only the eight final findings, their regressions, and the directly related documentation.

## Re-review timeout blocker

The first final-wave implementation registered a PowerShell scriptblock as a cancellation-token callback. That callback could run on a timer/thread-pool thread without a PowerShell runspace and therefore could fail to terminate a hung Wrangler process. RED: the new focused static contract failed on the committed publisher because it had no injectable authenticated-read deadline and still used `.Token.Register([Action] { ... })`. The behavioral harness regression was also written first; it emits partial stdout, launches a descendant `pwsh`, hangs, and requires a controlled timeout, full-tree termination, awaited exit, and scratch cleanup. It cannot be executed on this Linux host because `pwsh` is unavailable.

GREEN: the publisher now uses a monotonic deadline and bounded waits for asynchronous stdout reads, concurrent stderr draining, process exit, and diagnostics. When a deadline expires, the owning runspace synchronously calls `Kill(true)`, waits boundedly for exit and drain completion, deletes partial output, and returns the controlled timeout error. No PowerShell delegate executes from a timer or thread-pool callback. The focused static contract and the complete 56-test signer/policy suite pass locally; the real hanging-descendant regression remains a required Windows CI gate.
