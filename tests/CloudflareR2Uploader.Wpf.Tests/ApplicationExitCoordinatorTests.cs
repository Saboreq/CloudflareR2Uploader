using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using CloudflareR2Uploader.Wpf.Services;
using CloudflareR2Uploader.Wpf.ViewModels.Dialogs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Wpf.Tests
{
    [TestClass]
    public sealed class ApplicationExitCoordinatorTests
    {
        private static readonly bool[] ConcurrentAuthorizationResults = { true, false };
        private static readonly bool[] TwoDiscardChoices = { false, false };

        // Break caught: returning after merely signaling cancellation allows shutdown to race
        // the exact active upload run's asynchronous cleanup.
        [TestMethod]
        public async Task PrepareAsync_RemainsIncompleteUntilQueueCleanupCompletes()
        {
            ControlledUploadQueueLifecycle queue = new();
            RecordingDialogService dialogs = new() { KeepPartsResult = true };
            ApplicationExitCoordinator coordinator = Coordinator(queue, dialogs);

            Task<bool> prepare = coordinator.PrepareAsync();
            await queue.StopStarted.Task;

            Assert.IsFalse(prepare.IsCompleted, "PrepareAsync must wait for queue cleanup.");
            Assert.AreEqual(true, queue.PreserveChoices.Single());

            queue.StopFinished.SetResult();
            Assert.IsTrue(await prepare);
        }

        // Break caught: tray and updater preparation must coalesce so only the owner can
        // advance to an external shutdown or installer action.
        [TestMethod]
        public async Task PrepareAsync_ConcurrentCallersShareWorkAndOnlyOwnerIsAuthorized()
        {
            ControlledUploadQueueLifecycle queue = new();
            TaskCompletionSource decisionStarted = NewSource();
            TaskCompletionSource decisionFinished = NewSource();
            RecordingDialogService dialogs = new()
            {
                KeepPartsHandler = async () =>
                {
                    decisionStarted.SetResult();
                    await decisionFinished.Task;
                    return true;
                }
            };
            ApplicationExitCoordinator coordinator = Coordinator(queue, dialogs);

            Task<bool> tray = coordinator.PrepareAsync();
            await decisionStarted.Task;
            Task<bool> updater = coordinator.PrepareAsync();

            Assert.AreEqual(1, dialogs.KeepPartsCount);
            decisionFinished.SetResult();
            await queue.StopStarted.Task;
            Assert.AreEqual(1, queue.StopCount);
            queue.StopFinished.SetResult();

            bool[] results = await Task.WhenAll(tray, updater);
            CollectionAssert.AreEquivalent(ConcurrentAuthorizationResults, results);
            Assert.AreEqual(1, dialogs.KeepPartsCount);
            Assert.AreEqual(1, queue.StopCount);
        }

        // Break caught: cancelling the keep/discard decision must release preparation
        // ownership so a later real exit can retry.
        [TestMethod]
        public async Task PrepareAsync_CancelledDecisionAllowsRetry()
        {
            ControlledUploadQueueLifecycle queue = new() { CompleteStopsImmediately = true };
            Queue<bool?> decisions = new(new bool?[] { null, true });
            RecordingDialogService dialogs = new()
            {
                KeepPartsHandler = () => Task.FromResult(decisions.Dequeue())
            };
            ApplicationExitCoordinator coordinator = Coordinator(queue, dialogs);

            Assert.IsFalse(await coordinator.PrepareAsync());
            Assert.IsTrue(await coordinator.PrepareAsync());
            Assert.AreEqual(2, dialogs.KeepPartsCount);
            Assert.AreEqual(1, queue.StopCount);
        }

        // Break caught: an indefinitely blocked cleanup must be externally bounded, keep the
        // app open with exact copy, and release ownership for a later retry.
        [TestMethod]
        public async Task PrepareAsync_WhenCleanupTimesOut_ShowsErrorAndAllowsRetry()
        {
            ControlledUploadQueueLifecycle queue = new();
            queue.StopHandler = async (_, cancellationToken) =>
            {
                int call = queue.StopCount;
                if (call == 1)
                    await new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
                        .Task.WaitAsync(cancellationToken);
            };
            RecordingDialogService dialogs = new() { KeepPartsResult = false };
            RecordingLog log = new();
            ApplicationExitCoordinator coordinator = new(
                queue, dialogs, log, TimeSpan.FromMilliseconds(10));

            bool first = await coordinator.PrepareAsync().WaitAsync(TimeSpan.FromSeconds(2));
            bool retry = await coordinator.PrepareAsync().WaitAsync(TimeSpan.FromSeconds(2));

            Assert.IsFalse(first);
            Assert.IsTrue(retry);
            CollectionAssert.AreEqual(TwoDiscardChoices, queue.PreserveChoices.ToArray());
            Assert.AreEqual(2, queue.StopCount);
            AssertErrorSurface(dialogs, log);
        }

        // Break caught: cleanup I/O failure must use the same exact safe-open error surface as
        // timeout and never authorize the caller.
        [TestMethod]
        public async Task PrepareAsync_WhenCleanupThrowsIOException_ShowsExactErrorAndReturnsFalse()
        {
            ControlledUploadQueueLifecycle queue = new()
            {
                StopHandler = (_, _) => Task.FromException(new IOException("cleanup failed"))
            };
            RecordingDialogService dialogs = new() { KeepPartsResult = true };
            RecordingLog log = new();
            ApplicationExitCoordinator coordinator = Coordinator(queue, dialogs, log);

            Assert.IsFalse(await coordinator.PrepareAsync());
            AssertErrorSurface(dialogs, log);
        }

        [TestMethod]
        public async Task PrepareAsync_WhenCleanupThrowsInvalidOperation_ShowsExactErrorAndReturnsFalse()
        {
            ControlledUploadQueueLifecycle queue = new()
            {
                StopHandler = (_, _) => Task.FromException(new InvalidOperationException("cleanup state failed"))
            };
            RecordingDialogService dialogs = new() { KeepPartsResult = true };
            RecordingLog log = new();
            ApplicationExitCoordinator coordinator = Coordinator(queue, dialogs, log);

            Assert.IsFalse(await coordinator.PrepareAsync());
            AssertErrorSurface(dialogs, log);
        }

        [TestMethod]
        public async Task PrepareAsync_WhenKeepPartsDialogThrows_PropagatesWithoutShowingCleanupError()
        {
            ControlledUploadQueueLifecycle queue = new();
            RecordingDialogService dialogs = new()
            {
                KeepPartsHandler = () => Task.FromException<bool?>(
                    new InvalidOperationException("dialog failed"))
            };
            RecordingLog log = new();
            ApplicationExitCoordinator coordinator = Coordinator(queue, dialogs, log);

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => coordinator.PrepareAsync());

            Assert.AreEqual(0, dialogs.ErrorCount);
            Assert.AreEqual(0, log.ErrorCount);
        }

        // Break caught: authorization must be consumed once by Commit, and a committed
        // coordinator must reject every later preparation request.
        [TestMethod]
        public async Task Commit_ConsumesAuthorizationOnceAndRejectsLaterPreparation()
        {
            ControlledUploadQueueLifecycle queue = new() { IsRunning = false };
            ApplicationExitCoordinator coordinator = Coordinator(queue, new RecordingDialogService());
            int committed = 0;
            coordinator.ExitCommitted += (_, _) => committed++;

            Assert.IsTrue(await coordinator.PrepareAsync());
            coordinator.Commit();
            coordinator.Commit();

            Assert.AreEqual(1, committed);
            Assert.IsFalse(await coordinator.PrepareAsync());
        }

        [TestMethod]
        public async Task Commit_ThrowingSubscriberDoesNotBlockLaterSubscribersOrEscape()
        {
            ControlledUploadQueueLifecycle queue = new() { IsRunning = false };
            RecordingLog log = new();
            ApplicationExitCoordinator coordinator = Coordinator(queue, new RecordingDialogService(), log);
            int laterCalls = 0;
            coordinator.ExitCommitted += (_, _) => throw new InvalidOperationException("subscriber failed");
            coordinator.ExitCommitted += (_, _) => laterCalls++;

            Assert.IsTrue(await coordinator.PrepareAsync());
            coordinator.Commit();
            coordinator.Commit();
            coordinator.ReleasePreparation();

            Assert.AreEqual(1, laterCalls);
            Assert.AreEqual(1, log.ErrorCount);
            Assert.IsFalse(await coordinator.PrepareAsync());
        }

        [TestMethod]
        public async Task PrepareAsync_UnexpectedOwnerFailureReleasesSharedStateForRetry()
        {
            ControlledUploadQueueLifecycle queue = new();
            TaskCompletionSource entered = NewSource();
            TaskCompletionSource release = NewSource();
            int decisions = 0;
            RecordingDialogService dialogs = new()
            {
                KeepPartsHandler = async () =>
                {
                    if (Interlocked.Increment(ref decisions) == 1)
                    {
                        entered.SetResult();
                        await release.Task;
                        throw new InvalidOperationException("decision failed");
                    }
                    return true;
                }
            };
            ApplicationExitCoordinator coordinator = Coordinator(queue, dialogs);

            Task<bool> owner = coordinator.PrepareAsync();
            await entered.Task;
            Task<bool> joiner = coordinator.PrepareAsync();
            release.SetResult();

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => owner);
            Assert.IsFalse(await joiner);
            queue.CompleteStopsImmediately = true;
            Assert.IsTrue(await coordinator.PrepareAsync());
            Assert.AreEqual(2, decisions);
        }

        [TestMethod]
        public async Task CommitAndReleaseDuringPreparationDoNotConsumeFutureAuthorization()
        {
            ControlledUploadQueueLifecycle queue = new() { IsRunning = false };
            TaskCompletionSource entered = NewSource();
            TaskCompletionSource release = NewSource();
            RecordingDialogService dialogs = new();
            ApplicationExitCoordinator coordinator = Coordinator(queue, dialogs);
            int committed = 0;
            coordinator.ExitCommitted += (_, _) => committed++;

            queue.IsRunning = true;
            dialogs.KeepPartsHandler = async () =>
            {
                entered.SetResult();
                await release.Task;
                return true;
            };
            queue.CompleteStopsImmediately = true;
            Task<bool> preparation = coordinator.PrepareAsync();
            await entered.Task;

            coordinator.Commit();
            coordinator.ReleasePreparation();
            release.SetResult();
            Assert.IsTrue(await preparation);
            coordinator.Commit();

            Assert.AreEqual(1, committed);
            Assert.IsFalse(await coordinator.PrepareAsync());
        }

        // Break caught: an authorized updater whose installer fails to launch must release its
        // unused authorization so tray exit can subsequently prepare.
        [TestMethod]
        public async Task ReleasePreparation_AllowsRetryWhenAuthorizedActionDoesNotCommit()
        {
            ControlledUploadQueueLifecycle queue = new() { IsRunning = false };
            ApplicationExitCoordinator coordinator = Coordinator(queue, new RecordingDialogService());

            Assert.IsTrue(await coordinator.PrepareAsync());
            coordinator.ReleasePreparation();

            Assert.IsTrue(await coordinator.PrepareAsync());
        }

        private static ApplicationExitCoordinator Coordinator(
            ControlledUploadQueueLifecycle queue,
            RecordingDialogService dialogs,
            RecordingLog? log = null) =>
            new(queue, dialogs, log ?? new RecordingLog(), TimeSpan.FromSeconds(45));

        private static void AssertErrorSurface(RecordingDialogService dialogs, RecordingLog log)
        {
            Assert.AreEqual(1, dialogs.ErrorCount);
            Assert.AreEqual("The app is still finishing an upload", dialogs.ErrorTitle);
            Assert.AreEqual(
                "Cloudflare R2 Uploader stayed open so resumable upload state is not lost.",
                dialogs.ErrorMessage);
            Assert.AreEqual("App.Exit", log.ErrorOperation);
            Assert.AreEqual("Upload cleanup did not complete before exit.", log.ErrorMessage);
        }

        private static TaskCompletionSource NewSource() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private sealed class ControlledUploadQueueLifecycle : IUploadQueueLifecycle
        {
            private int _stopCount;

            public TaskCompletionSource StopStarted { get; } = NewSource();
            public TaskCompletionSource StopFinished { get; } = NewSource();
            public bool IsRunning { get; set; } = true;
            public bool CompleteStopsImmediately { get; set; }
            public int StopCount => Volatile.Read(ref _stopCount);
            public List<bool> PreserveChoices { get; } = new();
            public Func<bool, CancellationToken, Task>? StopHandler { get; set; }
            public IReadOnlyList<UploadQueueItem> GetItems() => Array.Empty<UploadQueueItem>();

            public async Task StopAsync(bool preserveParts, CancellationToken cancellationToken)
            {
                lock (PreserveChoices) PreserveChoices.Add(preserveParts);
                Interlocked.Increment(ref _stopCount);
                StopStarted.TrySetResult();
                if (StopHandler is not null)
                    await StopHandler(preserveParts, cancellationToken);
                else if (!CompleteStopsImmediately)
                    await StopFinished.Task.WaitAsync(cancellationToken);
            }
        }

        private sealed class RecordingLog : ILoggingService
        {
            public int ErrorCount { get; private set; }
            public string? ErrorOperation { get; private set; }
            public string? ErrorMessage { get; private set; }

            public void Debug(string operation, string message) { }
            public void Info(string operation, string message) { }
            public void Warning(string operation, string message) { }
            public void Error(string operation, string message, Exception exception)
            {
                ErrorCount++;
                ErrorOperation = operation;
                ErrorMessage = message;
            }
            public void HttpFailure(string operation, int statusCode, string awsErrorCode, string message, string objectKey, string uploadId) { }
            public string LogDirectory => Path.GetTempPath();
        }

        private sealed class RecordingDialogService : IDialogService
        {
            public bool? KeepPartsResult { get; set; }
            public Func<Task<bool?>>? KeepPartsHandler { get; set; }
            public int KeepPartsCount { get; private set; }
            public int ErrorCount { get; private set; }
            public string? ErrorTitle { get; private set; }
            public string? ErrorMessage { get; private set; }

            public Task ShowMessageAsync(string title, string message, string? details = null) => Task.CompletedTask;
            public Task ShowErrorAsync(string title, string message, string? technicalDetails = null)
            {
                ErrorCount++;
                ErrorTitle = title;
                ErrorMessage = message;
                return Task.CompletedTask;
            }
            public Task<bool> ConfirmAsync(string title, string message, string confirmLabel, bool isDestructive = false) => Task.FromResult(false);
            public Task<ConfirmationResult> ConfirmAsync(string title, string message, string confirmLabel, string optionLabel, bool isDestructive = false) => Task.FromResult(default(ConfirmationResult));
            public Task<string?> PromptForTextAsync(TextPromptRequest request) => Task.FromResult<string?>(null);
            public Task<OverwriteDecision> AskOverwriteAsync(string objectKey, long existingLength, DateTime existingModifiedUtc, long localLength) => Task.FromResult(OverwriteDecision.Skip);
            public Task<bool?> AskKeepPartsOnCancelAsync(int activeMultipartCount)
            {
                KeepPartsCount++;
                return KeepPartsHandler is null ? Task.FromResult(KeepPartsResult) : KeepPartsHandler();
            }
            public Task ShowTemporaryLinkAsync(TemporaryLinkDialogViewModel viewModel) => Task.CompletedTask;
            public Task<bool> ShowSettingsAsync(string? initialSection = null) => Task.FromResult(false);
            public string? BrowseForFolder(string title, string? initialDirectory = null) => null;
            public IReadOnlyList<string> BrowseForFiles(string title) => Array.Empty<string>();
            public string? BrowseForSaveFile(string title, string suggestedFileName, string filter) => null;
        }
    }
}
