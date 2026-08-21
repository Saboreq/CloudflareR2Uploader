using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using CloudflareR2Uploader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class OwnedDownloadCommitTests
    {
        [TestMethod]
        public void LinuxOverwrite_SubstitutionCannotPublishOrDeleteExternalFile()
        {
            if (!OperatingSystem.IsLinux()) return;
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string target = directory.File("report.txt");
                string external = directory.File("external.txt");
                string displacedOwnedLink = directory.File("displaced-owned-link");
                bool substitutionAttempted = false;
                File.WriteAllText(target, "existing-final");
                File.WriteAllText(external, "external-commit");

                using (OwnedDownloadCommit commit = OwnedDownloadCommit.Create(
                    target,
                    linkedPath =>
                    {
                        substitutionAttempted = true;
                        File.Move(linkedPath, displacedOwnedLink);
                        File.Move(external, linkedPath);
                        File.WriteAllText(external, "external-replacement");
                    }))
                {
                    using (StreamWriter writer = new StreamWriter(commit.Stream, leaveOpen: true))
                    {
                        writer.Write("download");
                    }
                    commit.Stream.Flush(true);

                    Assert.ThrowsException<IOException>(() => commit.TryPublish(target, true));
                }

                Assert.IsFalse(substitutionAttempted);
                Assert.AreEqual("existing-final", File.ReadAllText(target));
                Assert.AreEqual("external-commit", File.ReadAllText(external));
                Assert.IsFalse(File.Exists(displacedOwnedLink));
                Assert.AreEqual(2, Directory.GetFiles(directory.Path).Length);
            }
        }

        [TestMethod]
        public void CleanupFailure_DoesNotMaskPrimaryExceptionAndIsRetained()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string ownedPath = directory.File("owned.tmp");
                FileStream stream = new FileStream(
                    ownedPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Delete);
                OwnedDownloadCommit commit = new OwnedDownloadCommit(
                    stream, new FailingNativeOperations(nativeError: 5));
                OperationCanceledException primary = new OperationCanceledException("primary cancellation");
                Exception observed = null;

                try
                {
                    using (commit)
                    {
                        throw primary;
                    }
                }
                catch (Exception ex)
                {
                    observed = ex;
                }
                finally
                {
                    if (File.Exists(ownedPath)) File.Delete(ownedPath);
                }

                Assert.AreSame(primary, observed);
                Assert.IsNotNull(commit.CleanupFailure);
                StringAssert.Contains(commit.CleanupFailure.Message, "5");
            }
        }

        [TestMethod]
        public void StreamCloseFailure_DoesNotMaskPrimaryAndRetainsEveryCleanupFailure()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string ownedPath = directory.File("owned.tmp");
                IOException closeFailure = new IOException("injected close failure");
                ThrowAfterCloseFileStream stream = new ThrowAfterCloseFileStream(
                    ownedPath, closeFailure);
                SafeFileHandle ownedHandle = stream.SafeFileHandle;
                OwnedDownloadCommit commit = new OwnedDownloadCommit(
                    stream, new FailingNativeOperations(nativeError: 5));
                OperationCanceledException primary = new OperationCanceledException("primary cancellation");
                Exception observed = null;

                try
                {
                    using (commit)
                    {
                        throw primary;
                    }
                }
                catch (Exception ex)
                {
                    observed = ex;
                }
                finally
                {
                    if (File.Exists(ownedPath)) File.Delete(ownedPath);
                }

                Assert.AreSame(primary, observed);
                Assert.IsTrue(ownedHandle.IsClosed);
                Assert.IsInstanceOfType<AggregateException>(commit.CleanupFailure);
                AggregateException cleanup = (AggregateException)commit.CleanupFailure;
                Assert.AreEqual(2, cleanup.InnerExceptions.Count);
                StringAssert.Contains(cleanup.InnerExceptions[0].Message, "5");
                Assert.AreSame(closeFailure, cleanup.InnerExceptions[1]);
            }
        }

        [TestMethod]
        public async Task Dispose_RepeatedAndConcurrentUnpublishedCallsRunCleanupExactlyOnce()
        {
            using TemporaryDirectory directory = new TemporaryDirectory();
            string ownedPath = directory.File("owned.tmp");
            FileStream stream = new FileStream(
                ownedPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Delete);
            CountingNativeOperations native = new CountingNativeOperations(abandonResult: false, nativeError: 5);
            int streamDisposals = 0;
            OwnedDownloadCommit commit = new OwnedDownloadCommit(
                stream,
                native,
                owned =>
                {
                    Interlocked.Increment(ref streamDisposals);
                    owned.Dispose();
                });

            await Task.WhenAll(
                Task.Run(commit.Dispose),
                Task.Run(commit.Dispose),
                Task.Run(commit.Dispose));
            Exception firstOutcome = commit.CleanupFailure;
            commit.Dispose();

            Assert.AreEqual(1, native.AbandonCount);
            Assert.AreEqual(1, streamDisposals);
            Assert.AreSame(firstOutcome, commit.CleanupFailure);
            Assert.IsNotNull(firstOutcome);
        }

        [TestMethod]
        public async Task Dispose_RepeatedAndConcurrentPublishedCallsCloseStreamExactlyOnce()
        {
            using TemporaryDirectory directory = new TemporaryDirectory();
            string ownedPath = directory.File("owned.tmp");
            string target = directory.File("published.bin");
            FileStream stream = new FileStream(
                ownedPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Delete);
            CountingNativeOperations native = new CountingNativeOperations(abandonResult: true, nativeError: 0);
            int streamDisposals = 0;
            OwnedDownloadCommit commit = new OwnedDownloadCommit(
                stream,
                native,
                owned =>
                {
                    Interlocked.Increment(ref streamDisposals);
                    owned.Dispose();
                });
            Assert.IsTrue(commit.TryPublish(target, overwrite: false));

            await Task.WhenAll(Task.Run(commit.Dispose), Task.Run(commit.Dispose));
            commit.Dispose();

            Assert.AreEqual(0, native.AbandonCount);
            Assert.AreEqual(1, streamDisposals);
            Assert.IsNull(commit.CleanupFailure);
        }

        [TestMethod]
        public void WindowsCleanup_RenamedOwnedObjectIsRemovedAndReplacementSurvives()
        {
            if (!OperatingSystem.IsWindows()) return;
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string target = directory.File("report.txt");
                string displacedOwnedCommit = directory.File("owned-displaced.tmp");
                OwnedDownloadCommit commit = OwnedDownloadCommit.Create(target, null);
                string commitPath = Directory.GetFiles(directory.Path, ".r2c-*")[0];
                File.Move(commitPath, displacedOwnedCommit);
                File.WriteAllText(commitPath, "external-replacement");

                commit.Dispose();

                Assert.IsNull(commit.CleanupFailure);
                Assert.IsFalse(File.Exists(displacedOwnedCommit));
                Assert.AreEqual("external-replacement", File.ReadAllText(commitPath));
            }
        }

        [TestMethod]
        public void WindowsCommitName_PreservesComponentBudget()
        {
            string name = OwnedDownloadCommit.CreateWindowsCommitFileName();

            StringAssert.StartsWith(name, ".r2c-");
            Assert.IsTrue(name.Length <= 25, "Commit sibling name was " + name.Length + " characters.");
        }

        [TestMethod]
        public void WindowsCommitFlags_DoNotMarkFinalTemporary()
        {
            Assert.AreEqual(0u, OwnedDownloadCommit.WindowsCommitCreateFlags & 0x00000100u);
        }

        [TestMethod]
        public void WindowsCommitPath_PrefixesAfterSiblingComposition()
        {
            string unprefixedDirectory = "C:\\" + new string('d', 237);

            string commitPath = OwnedDownloadCommit.CreateWindowsCommitPath(unprefixedDirectory);

            Assert.AreEqual(240, unprefixedDirectory.Length);
            StringAssert.StartsWith(commitPath, "\\\\?\\");
            Assert.IsTrue(commitPath.Length > 260);
        }

        [DataTestMethod]
        [DataRow(@"C:\downloads\report.txt", @"\\?\C:\downloads\report.txt")]
        [DataRow(@"C:/downloads/report.txt", @"\\?\C:\downloads\report.txt")]
        [DataRow(@"\\server\share\report.txt", @"\\?\UNC\server\share\report.txt")]
        [DataRow(@"\\?\C:\downloads\report.txt", @"\\?\C:\downloads\report.txt")]
        [DataRow(@"\\?\UNC\server\share\report.txt", @"\\?\UNC\server\share\report.txt")]
        public void WindowsNativePublishPath_EncodesEveryAbsoluteDosOrUncTarget(
            string target,
            string expected)
        {
            Assert.AreEqual(expected, OwnedDownloadCommit.ToWindowsNativePublishPath(target));
        }

        [TestMethod]
        public void WindowsNativePublishPath_RejectsRelativeAndInvalidTargets()
        {
            string[] invalidTargets =
            {
                null,
                string.Empty,
                "report.txt",
                @"C:report.txt",
                @"\rooted.txt",
                @"\\server",
                @"\\server\share",
                @"\\?\relative",
                @"\\.\C:\report.txt",
                "C:\\bad\0name.txt"
            };

            foreach (string target in invalidTargets)
            {
                Assert.ThrowsException<ArgumentException>(
                    () => OwnedDownloadCommit.ToWindowsNativePublishPath(target),
                    "Target was accepted: " + (target ?? "<null>"));
            }
        }

        [TestMethod]
        public void WindowsShortNonOverwrite_PublishesExactFinalPath()
        {
            if (!OperatingSystem.IsWindows()) return;
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string target = directory.File("short-new.txt");
                using (OwnedDownloadCommit commit = OwnedDownloadCommit.Create(target, null))
                {
                    using (StreamWriter writer = new StreamWriter(commit.Stream, leaveOpen: true))
                    {
                        writer.Write("download");
                    }
                    commit.Stream.Flush(true);

                    Assert.IsTrue(commit.TryPublish(target, false));
                }

                Assert.AreEqual("download", File.ReadAllText(target));
                Assert.AreEqual(0, Directory.GetFiles(directory.Path, ".r2c-*").Length);
            }
        }

        [TestMethod]
        public void WindowsShortNonOverwrite_CollisionCanRetryAtExactAlternatePath()
        {
            if (!OperatingSystem.IsWindows()) return;
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string occupiedTarget = directory.File("occupied.txt");
                string retryTarget = directory.File("retry.txt");
                File.WriteAllText(occupiedTarget, "external");
                using (OwnedDownloadCommit commit = OwnedDownloadCommit.Create(occupiedTarget, null))
                {
                    using (StreamWriter writer = new StreamWriter(commit.Stream, leaveOpen: true))
                    {
                        writer.Write("download");
                    }
                    commit.Stream.Flush(true);

                    Assert.IsFalse(commit.TryPublish(occupiedTarget, false));
                    Assert.IsTrue(commit.TryPublish(retryTarget, false));
                }

                Assert.AreEqual("external", File.ReadAllText(occupiedTarget));
                Assert.AreEqual("download", File.ReadAllText(retryTarget));
                Assert.AreEqual(0, Directory.GetFiles(directory.Path, ".r2c-*").Length);
            }
        }

        [TestMethod]
        public void WindowsLongDirectoryAndNearComponent_PublishesExactFinalPath()
        {
            if (!OperatingSystem.IsWindows()) return;
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string longDirectory = Path.Combine(
                    directory.Path,
                    new string('a', 100),
                    new string('b', 100),
                    new string('c', 100));
                Directory.CreateDirectory(longDirectory);
                string target = Path.Combine(longDirectory, new string('x', 240));
                using (OwnedDownloadCommit commit = OwnedDownloadCommit.Create(target, null))
                {
                    using (StreamWriter writer = new StreamWriter(commit.Stream, leaveOpen: true))
                    {
                        writer.Write("download");
                    }
                    commit.Stream.Flush(true);
                    Assert.IsTrue(commit.TryPublish(target, false));
                }

                Assert.AreEqual("download", File.ReadAllText(target));
            }
        }

        [TestMethod]
        public void WindowsTargetBelowPrefixThreshold_WithLongerCommitSibling_PublishesExactly()
        {
            if (!OperatingSystem.IsWindows()) return;
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string longDirectory = directory.Path;
                while (Path.Combine(longDirectory, "x").Length < 240)
                {
                    int componentLength = Math.Min(
                        80,
                        238 - Path.Combine(longDirectory, "x").Length);
                    if (componentLength < 1) break;
                    longDirectory = Path.Combine(longDirectory, new string('d', componentLength));
                }
                Directory.CreateDirectory(longDirectory);
                string target = Path.Combine(longDirectory, "x");
                string rawCommitSibling = Path.Combine(longDirectory, new string('c', 25));

                Assert.AreEqual(239, target.Length);
                Assert.AreEqual(263, rawCommitSibling.Length);

                using (OwnedDownloadCommit commit = OwnedDownloadCommit.Create(target, null))
                {
                    using (StreamWriter writer = new StreamWriter(commit.Stream, leaveOpen: true))
                    {
                        writer.Write("download");
                    }
                    commit.Stream.Flush(true);
                    Assert.IsTrue(commit.TryPublish(target, false));
                }

                Assert.AreEqual("download", File.ReadAllText(target));
                Assert.AreEqual(0, Directory.GetFiles(longDirectory, ".r2c-*").Length);
            }
        }

        [TestMethod]
        public void WindowsShortOverwrite_ReplacesExactBytesWithoutCommitOrTemporaryAttribute()
        {
            if (!OperatingSystem.IsWindows()) return;
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string target = directory.File("report.txt");
                File.WriteAllText(target, "old-bytes");
                using (OwnedDownloadCommit commit = OwnedDownloadCommit.Create(target, null))
                {
                    using (StreamWriter writer = new StreamWriter(commit.Stream, leaveOpen: true))
                    {
                        writer.Write("new-bytes");
                    }
                    commit.Stream.Flush(true);

                    Assert.IsTrue(commit.TryPublish(target, true));
                }

                Assert.AreEqual("new-bytes", File.ReadAllText(target));
                Assert.AreEqual(0, Directory.GetFiles(directory.Path, ".r2c-*").Length);
                Assert.AreEqual(
                    0,
                    (int)(File.GetAttributes(target) & FileAttributes.Temporary));
            }
        }

        private sealed class FailingNativeOperations : IDownloadCommitNativeOperations
        {
            private readonly int _nativeError;

            public FailingNativeOperations(int nativeError)
            {
                _nativeError = nativeError;
            }

            public bool TryPublish(
                SafeFileHandle handle,
                string target,
                bool overwrite,
                out int nativeError)
            {
                nativeError = _nativeError;
                return false;
            }

            public bool TryAbandon(SafeFileHandle handle, out int nativeError)
            {
                nativeError = _nativeError;
                return false;
            }
        }

        private sealed class CountingNativeOperations : IDownloadCommitNativeOperations
        {
            private readonly bool _abandonResult;
            private readonly int _nativeError;
            private int _abandonCount;

            public CountingNativeOperations(bool abandonResult, int nativeError)
            {
                _abandonResult = abandonResult;
                _nativeError = nativeError;
            }

            public int AbandonCount => Volatile.Read(ref _abandonCount);

            public bool TryPublish(
                SafeFileHandle handle,
                string target,
                bool overwrite,
                out int nativeError)
            {
                nativeError = 0;
                return true;
            }

            public bool TryAbandon(SafeFileHandle handle, out int nativeError)
            {
                Interlocked.Increment(ref _abandonCount);
                nativeError = _nativeError;
                return _abandonResult;
            }
        }

        private sealed class ThrowAfterCloseFileStream : FileStream
        {
            private readonly Exception _closeFailure;

            public ThrowAfterCloseFileStream(string path, Exception closeFailure)
                : base(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Delete)
            {
                _closeFailure = closeFailure;
            }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (disposing) throw _closeFailure;
            }
        }
    }
}
