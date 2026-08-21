using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class UploadQueueServiceTests
    {
        private static readonly bool[] RunningThenStopped = { true, false };
        private static readonly bool[] RunningOnly = { true };

        // Break caught: disposing a fully stopped generation must not relatch a synthetic stop,
        // overwrite the first preservation choice, or repeat cancellation publication.
        [TestMethod]
        public async Task StopFalseThenDispose_ReleasesCompletedLifecycleWithoutSecondStop()
        {
            using TemporaryDirectory directory = new();
            string file = directory.File("completed-stop.bin");
            File.WriteAllText(file, "completed-stop");
            RecordingLog log = new();
            TaskCompletionSource started = NewSource();
            CancellationToken runToken = default;
            int cancellationCount = 0;
            UploadQueueService queue = CreateQueue(directory, async cancellationToken =>
            {
                runToken = cancellationToken;
                started.SetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref cancellationCount);
                }
            }, log);
            queue.AddPaths(new[] { file }, new AppSettings());
            UploadQueueItem item = queue.GetItems().Single();
            int itemChanges = 0;
            item.Changed += (_, _) => Interlocked.Increment(ref itemChanges);
            Task run = queue.StartAsync();
            await started.Task;
            WaitHandle runWaitHandle = runToken.WaitHandle;

            await queue.StopAsync(false, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            await run;
            queue.Dispose();
            queue.Dispose();

            Assert.IsFalse(item.PreservePartsOnCancel);
            Assert.AreEqual(UploadItemStatus.Cancelled, item.Status);
            Assert.AreEqual(1, cancellationCount);
            Assert.AreEqual(1, itemChanges);
            Assert.AreEqual(1, log.CancellationInfoCount);
            Assert.ThrowsException<ObjectDisposedException>(() => runWaitHandle.WaitOne(0));
            Assert.ThrowsException<ObjectDisposedException>(() => queue.PauseController.Pause());
        }

        // Break caught: idle concurrent disposal must synchronously release resources without
        // manufacturing cancellation state, logging, or events for queued work.
        [TestMethod]
        public async Task Dispose_IdleConcurrentCallsReleaseSynchronouslyWithoutSyntheticStop()
        {
            using TemporaryDirectory directory = new();
            string file = directory.File("idle-dispose.bin");
            File.WriteAllText(file, "idle-dispose");
            RecordingLog log = new();
            UploadQueueService queue = CreateQueue(directory, _ => Task.CompletedTask, log);
            queue.AddPaths(new[] { file }, new AppSettings());
            UploadQueueItem item = queue.GetItems().Single();
            int itemChanges = 0;
            int runStateChanges = 0;
            item.Changed += (_, _) => Interlocked.Increment(ref itemChanges);
            queue.RunStateChanged += (_, _) => Interlocked.Increment(ref runStateChanges);

            queue.PauseController.Pause();
            TaskCompletionSource signalRelease = NewSource();
            EventHandler blockingPauseHandler = (_, _) =>
                signalRelease.Task.GetAwaiter().GetResult();
            queue.PauseController.PauseStateChanged += blockingPauseHandler;
            using Barrier callers = new(3);
            Task first = Task.Run(() => { callers.SignalAndWait(); queue.Dispose(); });
            Task second = Task.Run(() => { callers.SignalAndWait(); queue.Dispose(); });
            callers.SignalAndWait();
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
            queue.PauseController.PauseStateChanged -= blockingPauseHandler;

            bool resourcesDisposed;
            try
            {
                if (queue.PauseController.IsPaused) queue.PauseController.Resume();
                queue.PauseController.Pause();
                resourcesDisposed = false;
            }
            catch (ObjectDisposedException)
            {
                resourcesDisposed = true;
            }
            finally
            {
                signalRelease.TrySetResult();
            }

            await WaitUntilAsync(() => !queue.IsRunning).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(resourcesDisposed, "Dispose must release idle resources before returning.");
            Assert.IsFalse(item.PreservePartsOnCancel);
            Assert.AreEqual(UploadItemStatus.Queued, item.Status);
            Assert.AreEqual(0, itemChanges);
            Assert.AreEqual(0, log.CancellationInfoCount);
            Assert.AreEqual(0, runStateChanges);
        }

        // Break caught: every concurrent caller disposing a completed lifecycle must return
        // only after the winning caller's synchronous cleanup has finished.
        [TestMethod]
        public async Task Dispose_ConcurrentCompletedCallsEachReturnAfterSynchronousCleanup()
        {
            using TemporaryDirectory directory = new();
            BlockingErrorLog log = new();
            TaskCompletionSource started = NewSource();
            TaskCompletionSource faultRelease = NewSource();
            UploadQueueService queue = CreateQueue(directory, async _ =>
            {
                started.SetResult();
                await faultRelease.Task;
                throw new InvalidOperationException("completed runner fault");
            }, log);
            Task run = queue.StartAsync();
            await started.Task;
            faultRelease.SetResult();
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => run);

            Task winner = Task.Run(queue.Dispose);
            await log.ErrorEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using ManualResetEventSlim loserStarted = new();
            bool loserObservedDisposedResources = false;
            Thread loser = new(() =>
            {
                loserStarted.Set();
                queue.Dispose();
                try { queue.PauseController.Pause(); }
                catch (ObjectDisposedException) { loserObservedDisposedResources = true; }
            })
            {
                IsBackground = true
            };
            bool loserStartedInTime = false;
            bool loserReachedStableState = false;
            try
            {
                loser.Start();
                loserStartedInTime = loserStarted.Wait(TimeSpan.FromSeconds(5));
                if (loserStartedInTime)
                {
                    loserReachedStableState = SpinWait.SpinUntil(() =>
                    {
                        ThreadState state = loser.ThreadState;
                        return (state & ThreadState.Stopped) != 0 ||
                            (state & ThreadState.WaitSleepJoin) != 0;
                    }, TimeSpan.FromSeconds(5));
                }
            }
            finally
            {
                log.ErrorRelease.TrySetResult();
            }
            await winner.WaitAsync(TimeSpan.FromSeconds(5));
            bool loserJoined = loser.Join(TimeSpan.FromSeconds(5));

            Assert.IsTrue(loserStartedInTime);
            Assert.IsTrue(loserReachedStableState);
            Assert.IsTrue(loserJoined);
            Assert.IsTrue(loserObservedDisposedResources,
                "A duplicate completed Dispose must join the winner's synchronous cleanup.");
        }

        // Break caught: cleanup can synchronously log on the Dispose owner thread. A logger that
        // reenters Dispose must return, while callers on other threads still join the outer cleanup.
        [TestMethod]
        public async Task Dispose_CleanupLoggerReentryReturnsOnOwnerThreadAndOtherCallersJoin()
        {
            using TemporaryDirectory directory = new();
            ReentrantBlockingErrorLog log = new();
            TaskCompletionSource started = NewSource();
            TaskCompletionSource faultRelease = NewSource();
            UploadQueueService queue = CreateQueue(directory, async _ =>
            {
                started.SetResult();
                await faultRelease.Task;
                throw new InvalidOperationException("completed runner fault");
            }, log);
            log.Queue = queue;
            Task run = queue.StartAsync();
            await started.Task;
            faultRelease.SetResult();
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => run);

            Task outerDispose = Task.Run(queue.Dispose);
            await log.ReentrantDisposeReturned.Task.WaitAsync(TimeSpan.FromSeconds(5));

            using ManualResetEventSlim joinerStarted = new();
            bool joinerObservedDisposedResources = false;
            Thread joiner = new(() =>
            {
                joinerStarted.Set();
                queue.Dispose();
                try { queue.PauseController.Pause(); }
                catch (ObjectDisposedException) { joinerObservedDisposedResources = true; }
            })
            {
                IsBackground = true
            };
            bool joinerBlocked = false;
            try
            {
                joiner.Start();
                Assert.IsTrue(joinerStarted.Wait(TimeSpan.FromSeconds(5)));
                joinerBlocked = SpinWait.SpinUntil(() =>
                    (joiner.ThreadState & ThreadState.WaitSleepJoin) != 0,
                    TimeSpan.FromSeconds(5));
                Assert.IsFalse(joiner.Join(TimeSpan.Zero),
                    "A non-owner Dispose must wait for the outer cleanup.");
            }
            finally
            {
                log.ErrorRelease.TrySetResult();
            }

            await outerDispose.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(joiner.Join(TimeSpan.FromSeconds(5)));
            Assert.IsTrue(joinerBlocked);
            Assert.AreEqual(log.OwnerThreadId, log.ReentrantThreadId);
            Assert.AreEqual(1, log.ErrorCount);
            Assert.IsTrue(joinerObservedDisposedResources);
            if (queue.PauseController.IsPaused) queue.PauseController.Resume();
            Assert.ThrowsException<ObjectDisposedException>(() => queue.PauseController.Pause());
        }

        // Break caught: terminal StopAsync/CancelAll calls after completed disposal must not
        // install another generation or publish another cancellation phase.
        [TestMethod]
        public async Task StopAndCancelAll_AfterCompletedDisposeRemainTerminalNoOps()
        {
            using TemporaryDirectory directory = new();
            string file = directory.File("terminal-stop.bin");
            File.WriteAllText(file, "terminal-stop");
            RecordingLog log = new();
            TaskCompletionSource started = NewSource();
            using UploadQueueService queue = CreateQueue(directory, async cancellationToken =>
            {
                started.SetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                catch (OperationCanceledException) { }
            }, log);
            queue.AddPaths(new[] { file }, new AppSettings());
            UploadQueueItem item = queue.GetItems().Single();
            int itemChanges = 0;
            List<bool> states = new();
            item.Changed += (_, _) => Interlocked.Increment(ref itemChanges);
            Task run = queue.StartAsync();
            await started.Task;
            queue.RunStateChanged += (_, _) => states.Add(queue.IsRunning);

            await queue.StopAsync(false, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            await run;
            queue.CancelAll(true);
            await queue.StopAsync(true, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            queue.Dispose();
            await queue.StopAsync(true, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            queue.CancelAll(true);
            await queue.StopAsync(false, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.IsFalse(queue.IsRunning);
            Assert.IsFalse(item.PreservePartsOnCancel);
            Assert.AreEqual(UploadItemStatus.Cancelled, item.Status);
            Assert.AreEqual(1, itemChanges);
            Assert.AreEqual(1, log.CancellationInfoCount);
            CollectionAssert.AreEqual(RunningThenStopped, states);
        }

        [TestMethod]
        public async Task RunStateChanged_CompletedStopPublishesTerminalFalse()
        {
            using TemporaryDirectory directory = new();
            TaskCompletionSource started = NewSource();
            TaskCompletionSource finished = NewSource();
            using UploadQueueService queue = CreateQueue(directory, _ =>
            {
                started.SetResult();
                return finished.Task;
            });
            Task run = queue.StartAsync();
            await started.Task;
            finished.SetResult();
            await run;
            List<bool> states = new();
            queue.RunStateChanged += (_, _) => states.Add(queue.IsRunning);

            await queue.StopAsync(true, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            CollectionAssert.AreEqual(RunningThenStopped, states);
            Assert.IsFalse(queue.IsRunning);
        }

        [TestMethod]
        public async Task RunStateChanged_SyntheticStopPublishesTerminalFalseAndIsolatesSubscribers()
        {
            using TemporaryDirectory directory = new();
            RecordingLog log = new();
            using UploadQueueService queue = CreateQueue(directory, _ => Task.CompletedTask, log);
            List<bool> states = new();
            queue.RunStateChanged += (_, _) => throw new InvalidOperationException("subscriber failed");
            queue.RunStateChanged += (_, _) => states.Add(queue.IsRunning);

            await queue.StopAsync(true, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            CollectionAssert.AreEqual(RunningThenStopped, states);
            Assert.AreEqual(2, log.ErrorCount);
            Assert.IsFalse(queue.IsRunning);
        }

        [TestMethod]
        public async Task RunStateChanged_ActiveStopPublishesTrueUntilCleanupThenTerminalFalse()
        {
            using TemporaryDirectory directory = new();
            TaskCompletionSource started = NewSource();
            TaskCompletionSource cleanupStarted = NewSource();
            TaskCompletionSource cleanupRelease = NewSource();
            using UploadQueueService queue = CreateQueue(directory, async cancellationToken =>
            {
                started.SetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                catch (OperationCanceledException)
                {
                    cleanupStarted.SetResult();
                    await cleanupRelease.Task;
                }
            });
            Task run = queue.StartAsync();
            await started.Task;
            List<bool> states = new();
            TaskCompletionSource signalPublished = NewSource();
            queue.RunStateChanged += (_, _) =>
            {
                states.Add(queue.IsRunning);
                signalPublished.TrySetResult();
            };

            Task stop = queue.StopAsync(true, CancellationToken.None);
            await Task.WhenAll(cleanupStarted.Task, signalPublished.Task).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(stop.IsCompleted);
            CollectionAssert.AreEqual(RunningOnly, states);

            cleanupRelease.SetResult();
            await Task.WhenAll(stop, run).WaitAsync(TimeSpan.FromSeconds(5));
            CollectionAssert.AreEqual(RunningThenStopped, states);
            Assert.IsFalse(queue.IsRunning);
        }

        // Break caught: a nonblocking async terminal subscriber must receive the task for a real
        // replacement generation, not the old lifecycle that is invoking the subscriber.
        [TestMethod]
        public async Task RunStateChanged_TerminalAsyncHandlerRestartsExactlyOneGeneration()
        {
            using TemporaryDirectory directory = new();
            TaskCompletionSource firstStarted = NewSource();
            TaskCompletionSource secondStarted = NewSource();
            TaskCompletionSource secondRelease = NewSource();
            TaskCompletionSource<Task> restartedRun = NewTaskSource();
            TaskCompletionSource asyncHandlerCompleted = NewSource();
            int invocation = 0;
            int restartRequests = 0;
            int restartObservedStopped = 0;

            using UploadQueueService queue = CreateQueue(directory, async cancellationToken =>
            {
                if (Interlocked.Increment(ref invocation) == 1)
                {
                    firstStarted.SetResult();
                    try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                    catch (OperationCanceledException) { }
                    return;
                }

                secondStarted.SetResult();
                await secondRelease.Task;
            });
            Task firstRun = queue.StartAsync();
            await firstStarted.Task;
            queue.RunStateChanged += async (_, _) =>
            {
                if (queue.IsRunning || Interlocked.CompareExchange(ref restartRequests, 1, 0) != 0)
                    return;

                Interlocked.Exchange(ref restartObservedStopped, 1);
                await Task.Yield();
                Task replacement = queue.StartAsync();
                restartedRun.TrySetResult(replacement);
                await replacement;
                asyncHandlerCompleted.TrySetResult();
            };
            queue.RunStateChanged += (_, _) =>
            {
                if (!queue.IsRunning)
                    restartedRun.Task.GetAwaiter().GetResult();
            };

            Task stop = queue.StopAsync(true, CancellationToken.None);
            try
            {
                Task replacement = await restartedRun.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Task.WhenAll(firstRun, stop).WaitAsync(TimeSpan.FromSeconds(5));

                Assert.AreEqual(1, Volatile.Read(ref restartObservedStopped));
                Assert.AreEqual(2, Volatile.Read(ref invocation));
                Assert.IsFalse(replacement.IsCompleted);
            }
            finally
            {
                secondRelease.TrySetResult();
            }

            await (await restartedRun.Task).WaitAsync(TimeSpan.FromSeconds(5));
            await asyncHandlerCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(2, Volatile.Read(ref invocation));
        }

        // Break caught: a terminal subscriber synchronously waiting for a quick replacement must
        // not wait on the incomplete lifecycle that is currently invoking it.
        [TestMethod]
        public async Task RunStateChanged_TerminalHandlerCanSynchronouslyWaitForQuickRestart()
        {
            using TemporaryDirectory directory = new();
            TaskCompletionSource firstStarted = NewSource();
            TaskCompletionSource handlerReturned = NewSource();
            TaskCompletionSource<Task> restartedRun = NewTaskSource();
            int invocation = 0;
            int observedStopped = 0;
            bool synchronousWaitCompleted = false;

            using UploadQueueService queue = CreateQueue(directory, async cancellationToken =>
            {
                if (Interlocked.Increment(ref invocation) == 1)
                {
                    firstStarted.SetResult();
                    try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                    catch (OperationCanceledException) { }
                }
            });
            Task firstRun = queue.StartAsync();
            await firstStarted.Task;
            queue.RunStateChanged += (_, _) =>
            {
                if (queue.IsRunning || Interlocked.CompareExchange(ref observedStopped, 1, 0) != 0)
                    return;

                Task replacement = queue.StartAsync();
                restartedRun.TrySetResult(replacement);
                synchronousWaitCompleted = replacement.Wait(TimeSpan.FromSeconds(2));
                handlerReturned.TrySetResult();
            };

            Task stop = queue.StopAsync(true, CancellationToken.None);
            await handlerReturned.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task replacementRun = await restartedRun.Task;
            await Task.WhenAll(firstRun, stop, replacementRun).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.IsTrue(synchronousWaitCompleted,
                "The callback must synchronously wait on quick generation B, not generation A.");
            Assert.AreEqual(1, Volatile.Read(ref observedStopped));
            Assert.AreEqual(2, Volatile.Read(ref invocation));
        }

        // Break caught: StopAsync from the terminal callback is already observing the completed
        // stop. It must be a completed no-op rather than returning its own incomplete lifecycle.
        [TestMethod]
        public async Task StopAsync_TerminalHandlerSynchronousCallIsIdempotentNoOp()
        {
            using TemporaryDirectory directory = new();
            RecordingLog log = new();
            TaskCompletionSource started = NewSource();
            TaskCompletionSource handlerReturned = NewSource();
            List<bool> states = new();
            int terminalCalls = 0;
            bool synchronousStopCompleted = false;

            using UploadQueueService queue = CreateQueue(directory, async cancellationToken =>
            {
                started.SetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                catch (OperationCanceledException) { }
            }, log);
            Task run = queue.StartAsync();
            await started.Task;
            queue.RunStateChanged += (_, _) => states.Add(queue.IsRunning);
            queue.RunStateChanged += (_, _) =>
            {
                if (queue.IsRunning || Interlocked.Increment(ref terminalCalls) != 1) return;
                Task duplicateStop = queue.StopAsync(false, CancellationToken.None);
                synchronousStopCompleted = duplicateStop.Wait(TimeSpan.FromSeconds(2));
                handlerReturned.TrySetResult();
            };

            Task stop = queue.StopAsync(true, CancellationToken.None);
            await handlerReturned.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.WhenAll(run, stop).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.IsTrue(synchronousStopCompleted);
            Assert.AreEqual(1, terminalCalls);
            Assert.AreEqual(1, log.CancellationInfoCount);
            CollectionAssert.AreEqual(RunningThenStopped, states);
        }

        // Break caught: unrelated callers remain behind the old terminal notification, while the
        // callback that observed false may intentionally install B. Earlier subscribers see false;
        // later subscribers can then truthfully observe B running.
        [TestMethod]
        public async Task RunStateChanged_CallbackRestartPreservesExternalGateAndSubscriberState()
        {
            using TemporaryDirectory directory = new();
            TaskCompletionSource firstStarted = NewSource();
            TaskCompletionSource terminalEntered = NewSource();
            TaskCompletionSource terminalRelease = NewSource();
            TaskCompletionSource secondStarted = NewSource();
            TaskCompletionSource secondRelease = NewSource();
            TaskCompletionSource<Task> callbackRestart = NewTaskSource();
            List<bool> laterObservations = new();
            int invocation = 0;
            int beforeObservedStopped = 0;
            int restartObservedStopped = 0;
            int restartRequested = 0;
            CancellationToken firstToken = default;
            Exception firstTokenAccessError = null;

            using UploadQueueService queue = CreateQueue(directory, async cancellationToken =>
            {
                if (Interlocked.Increment(ref invocation) == 1)
                {
                    firstToken = cancellationToken;
                    firstStarted.SetResult();
                    try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                    catch (OperationCanceledException) { }
                    return;
                }

                secondStarted.SetResult();
                await secondRelease.Task;
            });
            Task firstRun = queue.StartAsync();
            await firstStarted.Task;
            queue.RunStateChanged += (_, _) =>
            {
                if (queue.IsRunning || Interlocked.CompareExchange(ref beforeObservedStopped, 1, 0) != 0)
                    return;
                terminalEntered.TrySetResult();
                terminalRelease.Task.GetAwaiter().GetResult();
            };
            queue.RunStateChanged += (_, _) =>
            {
                if (queue.IsRunning || Interlocked.CompareExchange(ref restartRequested, 1, 0) != 0)
                    return;
                Interlocked.Exchange(ref restartObservedStopped, 1);
                callbackRestart.TrySetResult(queue.StartAsync());
            };
            queue.RunStateChanged += (_, _) =>
            {
                if (Volatile.Read(ref restartRequested) != 0)
                {
                    laterObservations.Add(queue.IsRunning);
                    try { firstToken.Register(() => { }).Dispose(); }
                    catch (Exception ex) { firstTokenAccessError = ex; }
                }
            };

            Task stop = queue.StopAsync(true, CancellationToken.None);
            await terminalEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task externalStart = queue.StartAsync();
            Assert.IsFalse(externalStart.IsCompleted);
            Assert.AreEqual(1, Volatile.Read(ref invocation));

            try
            {
                terminalRelease.TrySetResult();
                Task replacement = await callbackRestart.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Task.WhenAll(firstRun, stop, externalStart).WaitAsync(TimeSpan.FromSeconds(5));

                Assert.AreNotSame(externalStart, replacement);
                Assert.AreEqual(1, Volatile.Read(ref beforeObservedStopped));
                Assert.AreEqual(1, Volatile.Read(ref restartObservedStopped));
                Assert.IsTrue(laterObservations.Count > 0);
                Assert.IsTrue(laterObservations.All(state => state),
                    "No later subscriber may see a fabricated true for generation A.");
                Assert.IsNull(firstTokenAccessError,
                    "Generation A resources must remain live until its terminal notification returns.");
                Assert.AreEqual(2, Volatile.Read(ref invocation));
            }
            finally
            {
                terminalRelease.TrySetResult();
                secondRelease.TrySetResult();
            }

            await (await callbackRestart.Task).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(2, Volatile.Read(ref invocation));
        }

        [TestMethod]
        public async Task StopAsync_CompletedGenerationGatesSignalBeforeAllowingReplacement()
        {
            using TemporaryDirectory directory = new();
            string queuedFile = directory.File("queued-after-a.bin");
            File.WriteAllText(queuedFile, "queued");
            TaskCompletionSource firstStarted = NewSource();
            TaskCompletionSource firstFinished = NewSource();
            TaskCompletionSource signalEntered = NewSource();
            TaskCompletionSource signalRelease = NewSource();
            TaskCompletionSource secondStarted = NewSource();
            TaskCompletionSource secondFinished = NewSource();
            Exception tokenAccessError = null;
            CancellationToken firstToken = default;
            int invocation = 0;

            using UploadQueueService queue = CreateQueue(directory, cancellationToken =>
            {
                if (Interlocked.Increment(ref invocation) == 1)
                {
                    firstToken = cancellationToken;
                    firstStarted.SetResult();
                    return firstFinished.Task;
                }

                secondStarted.SetResult();
                return secondFinished.Task;
            });
            Task firstRun = queue.StartAsync();
            await firstStarted.Task;
            firstFinished.SetResult();
            await firstRun;
            queue.AddPaths(new[] { queuedFile }, new AppSettings());
            queue.Pause();
            queue.PauseController.PauseStateChanged += (_, _) =>
            {
                signalEntered.SetResult();
                signalRelease.Task.GetAwaiter().GetResult();
                try { firstToken.Register(() => { }).Dispose(); }
                catch (Exception ex) { tokenAccessError = ex; }
            };

            TaskCompletionSource<Task> stopReturned = NewTaskSource();
            Task invocationTask = Task.Run(() =>
            {
                try { stopReturned.SetResult(queue.StopAsync(true, CancellationToken.None)); }
                catch (Exception ex) { stopReturned.SetException(ex); }
            });
            await signalEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task blockedStart = queue.StartAsync();
            Task stop = null;

            try
            {
                stop = await stopReturned.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.IsTrue(queue.IsRunning, "The open signal gate is part of the running lifecycle.");
                Assert.AreEqual(1, invocation, "Generation B must not launch across A's signal phase.");
                Assert.IsFalse(blockedStart.IsCompleted);
                Assert.AreEqual(UploadItemStatus.Queued, queue.GetItems().Single().Status);
                Assert.IsNull(tokenAccessError);
            }
            finally
            {
                signalRelease.TrySetResult();
            }

            await invocationTask;
            await Task.WhenAll(stop, blockedStart);
            Assert.IsFalse(queue.IsRunning);
            Assert.AreEqual(UploadItemStatus.Cancelled, queue.GetItems().Single().Status);
            Assert.IsNull(tokenAccessError);

            Task secondRun = queue.StartAsync();
            await secondStarted.Task;
            Assert.AreEqual(2, invocation);
            secondFinished.SetResult();
            await secondRun;
        }

        [TestMethod]
        public async Task StopAsync_WithoutPriorGenerationUsesServiceSignalGate()
        {
            using TemporaryDirectory directory = new();
            string file = directory.File("standalone.bin");
            File.WriteAllText(file, "standalone");
            TaskCompletionSource signalEntered = NewSource();
            TaskCompletionSource signalRelease = NewSource();
            TaskCompletionSource runStarted = NewSource();
            TaskCompletionSource runFinished = NewSource();
            int invocation = 0;
            using UploadQueueService queue = CreateQueue(directory, _ =>
            {
                Interlocked.Increment(ref invocation);
                runStarted.SetResult();
                return runFinished.Task;
            });
            queue.AddPaths(new[] { file }, new AppSettings());
            queue.Pause();
            queue.PauseController.PauseStateChanged += (_, _) =>
            {
                signalEntered.SetResult();
                signalRelease.Task.GetAwaiter().GetResult();
            };

            TaskCompletionSource<Task> stopReturned = NewTaskSource();
            Task invocationTask = Task.Run(() => stopReturned.SetResult(
                queue.StopAsync(false, CancellationToken.None)));
            await signalEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task blockedStart = queue.StartAsync();
            Task stop = null;

            try
            {
                stop = await stopReturned.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.IsTrue(queue.IsRunning);
                Assert.AreEqual(0, invocation);
                Assert.IsFalse(blockedStart.IsCompleted);
            }
            finally
            {
                signalRelease.TrySetResult();
            }

            await invocationTask;
            await Task.WhenAll(stop, blockedStart);
            Assert.IsFalse(queue.IsRunning);
            Assert.AreEqual(UploadItemStatus.Cancelled, queue.GetItems().Single().Status);

            Task run = queue.StartAsync();
            await runStarted.Task;
            runFinished.SetResult();
            await run;
        }

        [TestMethod]
        public async Task StopAsync_CallerCancellationDoesNotWaitForBlockingSignalCallback()
        {
            using TemporaryDirectory directory = new();
            TaskCompletionSource started = NewSource();
            TaskCompletionSource callbackEntered = NewSource();
            TaskCompletionSource callbackRelease = NewSource();
            TaskCompletionSource cleanupRelease = NewSource();
            using UploadQueueService queue = CreateQueue(directory, async cancellationToken =>
            {
                cancellationToken.Register(() =>
                {
                    callbackEntered.SetResult();
                    callbackRelease.Task.GetAwaiter().GetResult();
                });
                started.SetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                catch (OperationCanceledException) { await cleanupRelease.Task; }
            });
            Task run = queue.StartAsync();
            await started.Task;
            using CancellationTokenSource caller = new();

            TaskCompletionSource<Task> stopReturned = NewTaskSource();
            Task invocationTask = Task.Run(() => stopReturned.SetResult(queue.StopAsync(true, caller.Token)));
            await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task retry = null;
            try
            {
                Task first = await stopReturned.Task.WaitAsync(TimeSpan.FromSeconds(5));
                caller.Cancel();
                await Assert.ThrowsExceptionAsync<TaskCanceledException>(
                    () => first.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.IsTrue(queue.IsRunning);
                retry = queue.StopAsync(false, CancellationToken.None);
                Assert.IsFalse(retry.IsCompleted);
                Assert.IsTrue(queue.IsRunning);
            }
            finally
            {
                callbackRelease.TrySetResult();
                cleanupRelease.TrySetResult();
            }

            await invocationTask;
            await retry.WaitAsync(TimeSpan.FromSeconds(5));
            await run;
            Assert.IsFalse(queue.IsRunning);
        }

        [TestMethod]
        public async Task DisposeFirst_StopSecondKeepsDisposePreservationAndSharedLifecycle()
        {
            using TemporaryDirectory directory = new();
            string file = directory.File("dispose-first.bin");
            File.WriteAllText(file, "dispose-first");
            TaskCompletionSource started = NewSource();
            TaskCompletionSource cleanupStarted = NewSource();
            TaskCompletionSource cleanupRelease = NewSource();
            UploadQueueService queue = CreateQueue(directory, async cancellationToken =>
            {
                started.SetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                catch (OperationCanceledException)
                {
                    cleanupStarted.SetResult();
                    await cleanupRelease.Task;
                }
            });
            queue.AddPaths(new[] { file }, new AppSettings());
            Task run = queue.StartAsync();
            await started.Task;

            queue.Dispose();
            Task stop = queue.StopAsync(false, CancellationToken.None);
            await cleanupStarted.Task;

            Assert.IsTrue(queue.GetItems().Single().PreservePartsOnCancel);
            Assert.IsFalse(stop.IsCompleted);
            Assert.IsTrue(queue.IsRunning);
            cleanupRelease.SetResult();
            await Task.WhenAll(stop, run);
            Assert.IsFalse(queue.IsRunning);
            queue.Dispose();
        }

        [TestMethod]
        public async Task StopFirst_OnCompletedGenerationDefersDisposeAcrossSignalPhase()
        {
            using TemporaryDirectory directory = new();
            string queuedFile = directory.File("dispose-overlap.bin");
            File.WriteAllText(queuedFile, "dispose-overlap");
            TaskCompletionSource started = NewSource();
            TaskCompletionSource finished = NewSource();
            TaskCompletionSource firstCallbackEntered = NewSource();
            TaskCompletionSource secondCallbackEntered = NewSource();
            TaskCompletionSource callbacksRelease = NewSource();
            int callbacks = 0;
            UploadQueueService queue = CreateQueue(directory, _ =>
            {
                started.SetResult();
                return finished.Task;
            });
            Task run = queue.StartAsync();
            await started.Task;
            finished.SetResult();
            await run;
            queue.AddPaths(new[] { queuedFile }, new AppSettings());
            queue.Pause();
            queue.PauseController.PauseStateChanged += (_, _) =>
            {
                if (Interlocked.Increment(ref callbacks) == 1) firstCallbackEntered.SetResult();
                else secondCallbackEntered.SetResult();
                callbacksRelease.Task.GetAwaiter().GetResult();
            };

            Task stop = queue.StopAsync(true, CancellationToken.None);
            await firstCallbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            queue.Dispose();
            Task pauseAttempt = Task.Run(queue.PauseController.Pause);
            await secondCallbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.IsTrue(queue.IsRunning);
            Assert.IsFalse(stop.IsCompleted);
            Assert.AreEqual(UploadItemStatus.Queued, queue.GetItems().Single().Status);
            Assert.ThrowsException<ObjectDisposedException>(() => queue.StartAsync());
            callbacksRelease.SetResult();
            await Task.WhenAll(stop, pauseAttempt);
            Assert.IsFalse(queue.IsRunning);
            Assert.AreEqual(UploadItemStatus.Cancelled, queue.GetItems().Single().Status);
        }

        [TestMethod]
        public async Task StartFirst_AfterCompletedGenerationMakesStopOwnReplacement()
        {
            using TemporaryDirectory directory = new();
            string file = directory.File("start-first.bin");
            File.WriteAllText(file, "start-first");
            TaskCompletionSource firstStarted = NewSource();
            TaskCompletionSource firstFinished = NewSource();
            TaskCompletionSource secondStarted = NewSource();
            TaskCompletionSource secondCancelled = NewSource();
            int invocation = 0;
            using UploadQueueService queue = CreateQueue(directory, async cancellationToken =>
            {
                if (Interlocked.Increment(ref invocation) == 1)
                {
                    firstStarted.SetResult();
                    await firstFinished.Task;
                    return;
                }

                secondStarted.SetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                catch (OperationCanceledException) { secondCancelled.SetResult(); }
            });
            Task first = queue.StartAsync();
            await firstStarted.Task;
            firstFinished.SetResult();
            await first;
            queue.AddPaths(new[] { file }, new AppSettings());

            Task second = queue.StartAsync();
            await secondStarted.Task;
            Task stop = queue.StopAsync(false, CancellationToken.None);
            await Task.WhenAll(stop, second, secondCancelled.Task);

            Assert.AreEqual(2, invocation);
            Assert.IsFalse(queue.GetItems().Single().PreservePartsOnCancel);
            Assert.IsFalse(queue.IsRunning);
        }

        [TestMethod]
        public async Task Restart_ObservesFaultedCompositeLifecycleExactlyOnce()
        {
            using TemporaryDirectory directory = new();
            RecordingLog log = new();
            TaskCompletionSource firstStarted = NewSource();
            TaskCompletionSource secondStarted = NewSource();
            TaskCompletionSource secondFinished = NewSource();
            int invocation = 0;
            using UploadQueueService queue = CreateQueue(directory, async cancellationToken =>
            {
                if (Interlocked.Increment(ref invocation) == 1)
                {
                    firstStarted.SetResult();
                    try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                    catch (OperationCanceledException)
                    {
                        throw new InvalidOperationException("fault after cancellation");
                    }
                }
                else
                {
                    secondStarted.SetResult();
                    await secondFinished.Task;
                }
            }, log);
            Task first = queue.StartAsync();
            await firstStarted.Task;
            queue.CancelAll(true);
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => first);
            await WaitUntilAsync(() => !queue.IsRunning).WaitAsync(TimeSpan.FromSeconds(5));

            Task second = queue.StartAsync();
            await secondStarted.Task;
            Assert.AreEqual(1, log.ErrorCount);
            Assert.AreEqual("Queue.Run", log.ErrorOperation);
            Assert.AreEqual("The previous upload run faulted before restart.", log.ErrorMessage);

            secondFinished.SetResult();
            await second;
        }

        [TestMethod]
        public async Task StopAsync_BlocksReplacementUntilTheEntireSignalPhaseFinishes()
        {
            using TemporaryDirectory directory = new();
            TaskCompletionSource firstStarted = NewSource();
            TaskCompletionSource firstFinished = NewSource();
            TaskCompletionSource callbackEntered = NewSource();
            TaskCompletionSource callbackRelease = NewSource();
            TaskCompletionSource secondStarted = NewSource();
            TaskCompletionSource secondFinished = NewSource();
            Exception tokenAccessError = null;
            UploadQueueItem secondItem = null;
            int invocation = 0;

            using UploadQueueService queue = CreateQueue(directory, cancellationToken =>
            {
                if (Interlocked.Increment(ref invocation) == 1)
                {
                    cancellationToken.Register(() =>
                    {
                        firstFinished.SetResult();
                        callbackEntered.SetResult();
                        callbackRelease.Task.GetAwaiter().GetResult();
                        try { cancellationToken.Register(() => { }).Dispose(); }
                        catch (Exception ex) { tokenAccessError = ex; }
                    });
                    firstStarted.SetResult();
                    return firstFinished.Task;
                }

                secondItem.SetStatus(UploadItemStatus.Uploading);
                secondStarted.SetResult();
                return secondFinished.Task;
            });
            Task firstRun = queue.StartAsync();
            await firstStarted.Task;

            Task stop = Task.Run(async () => await queue.StopAsync(true, CancellationToken.None));
            await callbackEntered.Task;
            await firstRun.WaitAsync(TimeSpan.FromSeconds(5));
            string secondFile = directory.File("generation-b.bin");
            File.WriteAllText(secondFile, "generation-b");
            queue.AddPaths(new[] { secondFile }, new AppSettings());
            secondItem = queue.GetItems().Single();
            Task blockedStart = queue.StartAsync();

            Assert.IsFalse(blockedStart.IsCompleted);
            Assert.AreEqual(1, invocation);
            Assert.IsFalse(secondStarted.Task.IsCompleted);
            Assert.AreEqual(UploadItemStatus.Queued, secondItem.Status);

            callbackRelease.SetResult();
            await Task.WhenAll(stop, blockedStart, firstRun);
            Assert.IsNull(tokenAccessError);

            Task secondRun = queue.StartAsync();
            await secondStarted.Task;
            Assert.AreEqual(2, invocation);
            secondFinished.SetResult();
            await secondRun;
        }

        [TestMethod]
        public async Task StartAsync_ThrowingPauseAndRunStateSubscribersCannotStrandLaunch()
        {
            using TemporaryDirectory directory = new();
            RecordingLog log = new();
            TaskCompletionSource started = NewSource();
            UploadQueueService queue = CreateQueue(directory, _ =>
            {
                started.SetResult();
                return Task.CompletedTask;
            }, log);
            queue.Pause();
            queue.PauseController.PauseStateChanged += (_, _) => throw new InvalidOperationException("pause callback");
            queue.RunStateChanged += (_, _) => throw new InvalidOperationException("run callback");
            int laterHandlerCalls = 0;
            queue.RunStateChanged += (_, _) => Interlocked.Increment(ref laterHandlerCalls);

            Task run = queue.StartAsync();
            await Task.WhenAll(run, started.Task).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.IsFalse(queue.IsRunning);
            Assert.IsTrue(laterHandlerCalls > 0);
            Assert.IsTrue(log.ErrorCount >= 2);
            queue.Dispose();
        }

        [TestMethod]
        public async Task StopAsync_ThrowingResumeSubscriberStillSignalsAndCompletesCleanup()
        {
            using TemporaryDirectory directory = new();
            RecordingLog log = new();
            TaskCompletionSource started = NewSource();
            TaskCompletionSource cancelled = NewSource();
            using UploadQueueService queue = CreateQueue(directory, async cancellationToken =>
            {
                started.SetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                catch (OperationCanceledException) { cancelled.SetResult(); }
            }, log);
            Task run = queue.StartAsync();
            await started.Task;
            queue.Pause();
            queue.PauseController.PauseStateChanged += (_, _) => throw new InvalidOperationException("resume callback");

            await queue.StopAsync(true, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            await Task.WhenAll(run, cancelled.Task);
            Assert.IsTrue(log.ErrorCount > 0);
        }

        [TestMethod]
        public async Task Dispose_ThrowingResumeSubscriberStillCancelsAndCompletesCleanup()
        {
            using TemporaryDirectory directory = new();
            RecordingLog log = new();
            TaskCompletionSource started = NewSource();
            TaskCompletionSource cleanupStarted = NewSource();
            TaskCompletionSource cleanupRelease = NewSource();
            UploadQueueService queue = CreateQueue(directory, async cancellationToken =>
            {
                started.SetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                catch (OperationCanceledException)
                {
                    cleanupStarted.SetResult();
                    await cleanupRelease.Task;
                }
            }, log);
            Task run = queue.StartAsync();
            await started.Task;
            queue.Pause();
            queue.PauseController.PauseStateChanged += (_, _) => throw new InvalidOperationException("resume callback");

            queue.Dispose();
            await cleanupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(run.IsCompleted);
            Assert.IsTrue(log.ErrorCount > 0);

            cleanupRelease.SetResult();
            await run;
        }

        [TestMethod]
        public async Task StopThenDispose_KeepsFirstPreservationChoiceAndDisposesResourcesOnce()
        {
            using TemporaryDirectory directory = new();
            string file = directory.File("preserve.bin");
            File.WriteAllText(file, "preserve");
            TaskCompletionSource started = NewSource();
            TaskCompletionSource cleanupStarted = NewSource();
            TaskCompletionSource cleanupRelease = NewSource();
            UploadQueueService queue = CreateQueue(directory, async cancellationToken =>
            {
                started.SetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                catch (OperationCanceledException)
                {
                    cleanupStarted.SetResult();
                    await cleanupRelease.Task;
                }
            });
            queue.AddPaths(new[] { file }, new AppSettings());
            Task run = queue.StartAsync();
            await started.Task;

            Task stop = queue.StopAsync(false, CancellationToken.None);
            await cleanupStarted.Task;
            queue.Dispose();
            queue.Dispose();

            Assert.IsFalse(queue.GetItems().Single().PreservePartsOnCancel);
            queue.PauseController.Pause();
            queue.PauseController.Resume();
            cleanupRelease.SetResult();
            await Task.WhenAll(stop, run);
            Assert.ThrowsException<ObjectDisposedException>(() => queue.PauseController.Pause());
        }

        [TestMethod]
        public async Task StopAsync_ThrowingCancellationCallbackStillCompletesLifecycle()
        {
            using TemporaryDirectory directory = new();
            RecordingLog log = new();
            TaskCompletionSource started = NewSource();
            using UploadQueueService queue = CreateQueue(directory, async cancellationToken =>
            {
                using CancellationTokenRegistration registration =
                    cancellationToken.Register(() => throw new InvalidOperationException("callback failed"));
                started.SetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                catch (OperationCanceledException) { }
            }, log);
            Task run = queue.StartAsync();
            await started.Task;

            await queue.StopAsync(true, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            await run;

            Assert.AreEqual("Queue.Cancel", log.ErrorOperation);
            Assert.AreEqual("Upload cancellation callbacks failed.", log.ErrorMessage);
        }

        [TestMethod]
        public async Task Dispose_ObservesSettingsProviderFailureOutsideRunnerTryBlock()
        {
            using TemporaryDirectory directory = new();
            RecordingLog log = new();
            UploadQueueService queue = new(log, new UploadStateStore(log, directory.File("state")))
            {
                SettingsProvider = () => throw new InvalidOperationException("provider failed")
            };

            Task run = queue.StartAsync();
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => run);
            queue.Dispose();
            await log.ErrorLogged.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual("Queue.Dispose", log.ErrorOperation);
            Assert.AreEqual("The upload run faulted during shutdown cleanup.", log.ErrorMessage);
        }

        // Break caught: a retrying exit caller must not rewrite the preservation choice while
        // the same run is still performing cancellation cleanup.
        [TestMethod]
        public async Task StopAsync_RetryAfterCallerCancellationKeepsFirstGenerationChoice()
        {
            using TemporaryDirectory directory = new();
            string file = directory.File("retry.bin");
            File.WriteAllText(file, "retry");
            TaskCompletionSource runStarted = NewSource();
            TaskCompletionSource cleanupStarted = NewSource();
            TaskCompletionSource cleanupFinished = NewSource();

            using UploadQueueService queue = CreateQueue(directory, async cancellationToken =>
            {
                runStarted.SetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                catch (OperationCanceledException)
                {
                    cleanupStarted.SetResult();
                    await cleanupFinished.Task;
                }
            });
            queue.AddPaths(new[] { file }, new AppSettings());
            Task run = queue.StartAsync();
            await runStarted.Task;

            using CancellationTokenSource firstCaller = new();
            Task firstStop = queue.StopAsync(preserveParts: true, firstCaller.Token);
            await cleanupStarted.Task;
            firstCaller.Cancel();
            await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => firstStop);

            Task retry = queue.StopAsync(preserveParts: false, CancellationToken.None);

            Assert.IsFalse(retry.IsCompleted);
            Assert.IsTrue(queue.GetItems().Single().PreservePartsOnCancel,
                "The first choice must remain latched for the active generation.");

            cleanupFinished.SetResult();
            await retry;
            await run;
            Assert.IsTrue(queue.GetItems().Single().PreservePartsOnCancel);
        }

        // Break caught: duplicate stop requests must share one cancellation signal and one
        // immutable preservation decision for the active generation.
        [TestMethod]
        public async Task StopAsync_ConcurrentCallersShareOneLatchedStop()
        {
            using TemporaryDirectory directory = new();
            string file = directory.File("concurrent.bin");
            File.WriteAllText(file, "concurrent");
            TaskCompletionSource runStarted = NewSource();
            TaskCompletionSource cleanupStarted = NewSource();
            TaskCompletionSource cleanupFinished = NewSource();
            int cancellationCount = 0;

            using UploadQueueService queue = CreateQueue(directory, async cancellationToken =>
            {
                runStarted.SetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref cancellationCount);
                    cleanupStarted.SetResult();
                    await cleanupFinished.Task;
                }
            });
            queue.AddPaths(new[] { file }, new AppSettings());
            Task run = queue.StartAsync();
            await runStarted.Task;

            Task first = queue.StopAsync(preserveParts: false, CancellationToken.None);
            await cleanupStarted.Task;
            Task duplicate = queue.StopAsync(preserveParts: true, CancellationToken.None);

            Assert.AreEqual(1, cancellationCount);
            Assert.IsFalse(queue.GetItems().Single().PreservePartsOnCancel);
            Assert.IsFalse(first.IsCompleted);
            Assert.IsFalse(duplicate.IsCompleted);
            Task blockedStart = queue.StartAsync();
            Assert.IsFalse(blockedStart.IsCompleted,
                "A start while cleanup is active must stay on the stopping generation.");

            cleanupFinished.SetResult();
            await Task.WhenAll(first, duplicate, run);
        }

        // Break caught: capturing the run task after cancellation callbacks can make stop wait
        // for a newly started generation that it never cancelled.
        [TestMethod]
        public async Task StopAsync_CapturesCancelledGenerationBeforeAllowingNextRun()
        {
            using TemporaryDirectory directory = new();
            TaskCompletionSource firstStarted = NewSource();
            TaskCompletionSource firstFinished = NewSource();
            TaskCompletionSource secondStarted = NewSource();
            TaskCompletionSource secondFinished = NewSource();
            int invocation = 0;

            using UploadQueueService queue = CreateQueue(directory, cancellationToken =>
            {
                int current = Interlocked.Increment(ref invocation);
                if (current == 1)
                {
                    firstStarted.SetResult();
                    cancellationToken.Register(firstFinished.SetResult);
                    return firstFinished.Task;
                }

                secondStarted.SetResult();
                return secondFinished.Task;
            });
            Task firstRun = queue.StartAsync();
            await firstStarted.Task;

            Task stop = queue.StopAsync(preserveParts: true, CancellationToken.None);
            await stop;
            Assert.AreEqual(1, invocation);

            Task secondRun = queue.StartAsync();
            await secondStarted.Task;
            secondFinished.SetResult();
            await secondRun;
            Assert.AreEqual(2, invocation);
        }

        // Break caught: disposal beginning during an active run must reject every later start
        // without disposing synchronization resources still used by that run.
        [TestMethod]
        public async Task Dispose_DuringActiveRunRejectsStartAndDefersResourceDisposal()
        {
            using TemporaryDirectory directory = new();
            TaskCompletionSource runStarted = NewSource();
            TaskCompletionSource cleanupStarted = NewSource();
            TaskCompletionSource cleanupFinished = NewSource();
            int invocation = 0;
            UploadQueueService queue = CreateQueue(directory, async cancellationToken =>
            {
                Interlocked.Increment(ref invocation);
                runStarted.SetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                catch (OperationCanceledException)
                {
                    cleanupStarted.SetResult();
                    await cleanupFinished.Task;
                }
            });
            Task run = queue.StartAsync();
            await runStarted.Task;

            using Barrier callers = new(3);
            Task dispose = Task.Run(() =>
            {
                callers.SignalAndWait();
                queue.Dispose();
            });
            Task concurrentStart = Task.Run(() =>
            {
                callers.SignalAndWait();
                try
                {
                    Assert.AreSame(run, queue.StartAsync());
                }
                catch (ObjectDisposedException)
                {
                    // Disposal won the lifecycle lock; rejection is the other valid ordering.
                }
            });
            callers.SignalAndWait();
            await Task.WhenAll(dispose, concurrentStart);
            await cleanupStarted.Task;

            Assert.ThrowsException<ObjectDisposedException>(() => queue.StartAsync());
            Assert.AreEqual(1, invocation);
            queue.PauseController.Pause();
            queue.PauseController.Resume();

            cleanupFinished.SetResult();
            await run;
            Assert.ThrowsException<ObjectDisposedException>(() => queue.PauseController.Pause());
        }

        // Break caught: concurrent duplicate Dispose calls must schedule one deferred cleanup
        // and remain idempotent after that cleanup completes.
        [TestMethod]
        public async Task Dispose_ConcurrentAndRepeatedCallsAreIdempotent()
        {
            using TemporaryDirectory directory = new();
            TaskCompletionSource runStarted = NewSource();
            TaskCompletionSource cleanupStarted = NewSource();
            TaskCompletionSource cleanupFinished = NewSource();
            CancellationToken runToken = default;
            UploadQueueService queue = CreateQueue(directory, async cancellationToken =>
            {
                runToken = cancellationToken;
                runStarted.SetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                catch (OperationCanceledException)
                {
                    cleanupStarted.SetResult();
                    await cleanupFinished.Task;
                }
            });
            Task run = queue.StartAsync();
            await runStarted.Task;
            WaitHandle runWaitHandle = runToken.WaitHandle;
            using Barrier callers = new(3);

            Task first = Task.Run(() => { callers.SignalAndWait(); queue.Dispose(); });
            Task second = Task.Run(() => { callers.SignalAndWait(); queue.Dispose(); });
            callers.SignalAndWait();
            await Task.WhenAll(first, second);
            await cleanupStarted.Task;

            queue.PauseController.Pause();
            queue.PauseController.Resume();
            cleanupFinished.SetResult();
            await run;

            await queue.DisposalCompleted.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(queue.DisposalCompleted.IsCompletedSuccessfully);
            queue.Dispose();
            Assert.ThrowsException<ObjectDisposedException>(() => runWaitHandle.WaitOne(0));
            if (queue.PauseController.IsPaused) queue.PauseController.Resume();
            Assert.ThrowsException<ObjectDisposedException>(() => queue.PauseController.Pause());
        }

        // Break caught: a runner fault outside RunAsync's main try must be observed and logged
        // by dispose-only shutdown before lifecycle resources are released.
        [TestMethod]
        public async Task Dispose_FaultedRunObservesFaultAndDisposesAfterCompletion()
        {
            using TemporaryDirectory directory = new();
            RecordingLog log = new();
            TaskCompletionSource runStarted = NewSource();
            TaskCompletionSource faultReleased = NewSource();
            UploadQueueService queue = CreateQueue(directory, async _ =>
            {
                runStarted.SetResult();
                await faultReleased.Task;
                throw new InvalidOperationException("deterministic runner fault");
            }, log);
            Task run = queue.StartAsync();
            await runStarted.Task;

            queue.Dispose();
            queue.PauseController.Pause();
            queue.PauseController.Resume();
            faultReleased.SetResult();
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => run);
            await log.ErrorLogged.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual("Queue.Dispose", log.ErrorOperation);
            Assert.AreEqual("The upload run faulted during shutdown cleanup.", log.ErrorMessage);
            Assert.ThrowsException<ObjectDisposedException>(() => queue.PauseController.Pause());
        }

        // Break caught: cancellation must also defer resource disposal until the run's own
        // cleanup is done, without reporting cancellation as an unobserved fault.
        [TestMethod]
        public async Task Dispose_CancelledRunDisposesAfterCleanupWithoutFaultLog()
        {
            using TemporaryDirectory directory = new();
            RecordingLog log = new();
            TaskCompletionSource runStarted = NewSource();
            TaskCompletionSource cleanupStarted = NewSource();
            TaskCompletionSource cleanupFinished = NewSource();
            UploadQueueService queue = CreateQueue(directory, async cancellationToken =>
            {
                runStarted.SetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                catch (OperationCanceledException)
                {
                    cleanupStarted.SetResult();
                    await cleanupFinished.Task;
                    throw;
                }
            }, log);
            Task run = queue.StartAsync();
            await runStarted.Task;

            queue.Dispose();
            await cleanupStarted.Task;
            queue.PauseController.Pause();
            queue.PauseController.Resume();
            cleanupFinished.SetResult();
            await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => run);

            Assert.IsNull(log.ErrorOperation);
            Assert.ThrowsException<ObjectDisposedException>(() => queue.PauseController.Pause());
        }

        // Break caught: cancellation signaling that returns before the active run's cleanup
        // completes can let process exit dispose state that cleanup still needs.
        [TestMethod]
        public async Task StopAsync_WaitsForCancellationCleanupAndAppliesPreservationChoice()
        {
            using TemporaryDirectory directory = new TemporaryDirectory();
            string first = directory.File("first.bin");
            string second = directory.File("second.bin");
            File.WriteAllText(first, "first");
            File.WriteAllText(second, "second");

            TaskCompletionSource runStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource cleanupStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource cleanupFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
            bool cancellationObserved = false;

            UploadStateStore stateStore = new(null, directory.File("state"));
            using UploadQueueService queue = new(null, stateStore, async cancellationToken =>
            {
                runStarted.SetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    cancellationObserved = true;
                    cleanupStarted.SetResult();
                    await cleanupFinished.Task;
                }
            });
            queue.AddPaths(new[] { first, second }, new AppSettings());

            Task run = queue.StartAsync();
            await runStarted.Task;

            Task stop = queue.StopAsync(preserveParts: true, CancellationToken.None);
            await cleanupStarted.Task;

            Assert.IsFalse(stop.IsCompleted, "StopAsync must wait for cancellation cleanup.");
            Assert.IsTrue(cancellationObserved, "The active run must observe cancellation.");
            Assert.IsTrue(queue.GetItems().All(item => item.PreservePartsOnCancel));

            cleanupFinished.SetResult();
            await stop;
            await run;
        }

        [TestMethod]
        public void AddPaths_HandlesSmallLargeMultipleAndRecursiveFolderInputs()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string small = directory.File("small.txt");
                File.WriteAllText(small, "small");

                string large = directory.File("large.bin");
                using (FileStream stream = new FileStream(large, FileMode.CreateNew, FileAccess.Write))
                    stream.SetLength(301L * 1024 * 1024);

                string selectedFolder = directory.File("selected");
                string nestedFolder = Path.Combine(selectedFolder, "nested");
                Directory.CreateDirectory(nestedFolder);
                File.WriteAllText(Path.Combine(selectedFolder, "root.json"), "{}");
                File.WriteAllText(Path.Combine(nestedFolder, "child.txt"), "child");

                AppSettings settings = new AppSettings
                {
                    KeyPrefix = @"uploads\test",
                    PreserveFolderStructure = true
                };
                UploadStateStore stateStore =
                    new UploadStateStore(null, directory.File("state"));

                using (UploadQueueService queue = new UploadQueueService(null, stateStore))
                {
                    AddFilesResult result = queue.AddPaths(
                        new[] { small, large, selectedFolder, small },
                        settings);

                    Assert.AreEqual(4, result.Added);
                    Assert.AreEqual(1, result.Duplicates);
                    Assert.AreEqual(4, queue.Count);

                    UploadQueueItem largeItem = queue.GetItems().Single(item => item.FileName == "large.bin");
                    Assert.IsTrue(largeItem.WillUseMultipart(300L * 1024 * 1024));

                    string[] keys = queue.GetItems().Select(item => item.ObjectKey).ToArray();
                    CollectionAssert.Contains(keys, "uploads/test/small.txt");
                    CollectionAssert.Contains(keys, "uploads/test/large.bin");
                    CollectionAssert.Contains(keys, "uploads/test/selected/root.json");
                    CollectionAssert.Contains(keys, "uploads/test/selected/nested/child.txt");
                }
            }
        }

        private static UploadQueueService CreateQueue(
            TemporaryDirectory directory,
            Func<CancellationToken, Task> runOverride,
            ILoggingService log = null) =>
            new(log, new UploadStateStore(log, directory.File("state")), runOverride);

        private static TaskCompletionSource NewSource() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static TaskCompletionSource<Task> NewTaskSource() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            while (!condition()) await Task.Yield();
        }

        private sealed class RecordingLog : ILoggingService
        {
            private int _errorCount;
            private int _cancellationInfoCount;
            public TaskCompletionSource ErrorLogged { get; } = NewSource();
            public string ErrorOperation { get; private set; }
            public string ErrorMessage { get; private set; }
            public int ErrorCount => _errorCount;
            public int CancellationInfoCount => _cancellationInfoCount;

            public void Debug(string operation, string message) { }
            public void Info(string operation, string message)
            {
                if (operation == "Queue.Cancel")
                    Interlocked.Increment(ref _cancellationInfoCount);
            }
            public void Warning(string operation, string message) { }
            public void Error(string operation, string message, Exception exception)
            {
                Interlocked.Increment(ref _errorCount);
                ErrorOperation = operation;
                ErrorMessage = message;
                ErrorLogged.TrySetResult();
            }
            public void HttpFailure(string operation, int statusCode, string awsErrorCode, string message, string objectKey, string uploadId) { }
            public string LogDirectory => Path.GetTempPath();
        }

        private sealed class BlockingErrorLog : ILoggingService
        {
            public TaskCompletionSource ErrorEntered { get; } = NewSource();
            public TaskCompletionSource ErrorRelease { get; } = NewSource();

            public void Debug(string operation, string message) { }
            public void Info(string operation, string message) { }
            public void Warning(string operation, string message) { }
            public void Error(string operation, string message, Exception exception)
            {
                ErrorEntered.TrySetResult();
                ErrorRelease.Task.GetAwaiter().GetResult();
            }
            public void HttpFailure(string operation, int statusCode, string awsErrorCode, string message, string objectKey, string uploadId) { }
            public string LogDirectory => Path.GetTempPath();
        }

        private sealed class ReentrantBlockingErrorLog : ILoggingService
        {
            private int _errorCount;
            public UploadQueueService Queue { get; set; }
            public TaskCompletionSource ReentrantDisposeReturned { get; } = NewSource();
            public TaskCompletionSource ErrorRelease { get; } = NewSource();
            public int OwnerThreadId { get; private set; }
            public int ReentrantThreadId { get; private set; }
            public int ErrorCount => _errorCount;

            public void Debug(string operation, string message) { }
            public void Info(string operation, string message) { }
            public void Warning(string operation, string message) { }
            public void Error(string operation, string message, Exception exception)
            {
                Interlocked.Increment(ref _errorCount);
                OwnerThreadId = Environment.CurrentManagedThreadId;
                Queue.Dispose();
                ReentrantThreadId = Environment.CurrentManagedThreadId;
                ReentrantDisposeReturned.TrySetResult();
                ErrorRelease.Task.GetAwaiter().GetResult();
            }
            public void HttpFailure(string operation, int statusCode, string awsErrorCode, string message, string objectKey, string uploadId) { }
            public string LogDirectory => Path.GetTempPath();
        }
    }
}
