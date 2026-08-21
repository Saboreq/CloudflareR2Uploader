#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class ActivityHistoryServiceTests
    {
        private static ActivityRecord Record(
            ActivityAction action,
            ActivityResult result = ActivityResult.Success,
            string target = "assets/builds/app.zip",
            long? size = 1024,
            DateTime? timestampUtc = null)
        {
            return new ActivityRecord
            {
                Action = action,
                Result = result,
                Target = target,
                BucketName = "storage",
                SizeBytes = size,
                DurationMilliseconds = 400,
                TimestampUtc = timestampUtc ?? DateTime.UtcNow
            };
        }

        [TestMethod]
        public async Task Record_AppendsAndReadsBack()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            using (ActivityHistoryService service = new ActivityHistoryService(null, directory.File("activity.jsonl")))
            {
                await service.RecordAsync(Record(ActivityAction.Upload));
                await service.RecordAsync(Record(ActivityAction.Download, target: "assets/builds/other.zip"));

                IReadOnlyList<ActivityRecord> all = await service.QueryAsync(new ActivityQuery());

                Assert.AreEqual(2, all.Count);
                Assert.IsTrue(all[0].TimestampUtc >= all[1].TimestampUtc, "newest first");
            }
        }

        [TestMethod]
        public async Task Record_NeverThrowsWhenTheFileCannotBeWritten()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                // A directory where the file should be makes every write fail.
                string path = directory.File("activity.jsonl");
                Directory.CreateDirectory(path);

                using (ActivityHistoryService service = new ActivityHistoryService(null, path))
                {
                    await service.RecordAsync(Record(ActivityAction.Upload));
                    Assert.AreEqual(0, (await service.QueryAsync(new ActivityQuery())).Count);
                }
            }
        }

        [TestMethod]
        public async Task Query_FiltersByCategoryErrorsRangeAndText()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            using (ActivityHistoryService service = new ActivityHistoryService(null, directory.File("activity.jsonl")))
            {
                DateTime now = DateTime.UtcNow;

                await service.RecordAsync(Record(ActivityAction.Upload, target: "assets/one.zip", timestampUtc: now));
                await service.RecordAsync(Record(ActivityAction.Download, target: "assets/two.zip", timestampUtc: now.AddDays(-2)));
                await service.RecordAsync(Record(ActivityAction.Delete, target: "assets/three.zip", timestampUtc: now.AddDays(-9)));
                await service.RecordAsync(Record(ActivityAction.Upload, ActivityResult.Failed, "assets/four.zip", timestampUtc: now));

                Assert.AreEqual(2, (await service.QueryAsync(new ActivityQuery { Category = ActivityCategory.Uploads })).Count);
                Assert.AreEqual(1, (await service.QueryAsync(new ActivityQuery { Category = ActivityCategory.Downloads })).Count);
                Assert.AreEqual(1, (await service.QueryAsync(new ActivityQuery { Category = ActivityCategory.Changes })).Count);
                Assert.AreEqual(1, (await service.QueryAsync(new ActivityQuery { ErrorsOnly = true })).Count);
                Assert.AreEqual(3, (await service.QueryAsync(new ActivityQuery { FromUtc = now.AddDays(-7) })).Count);
                Assert.AreEqual(1, (await service.QueryAsync(new ActivityQuery { SearchText = "THREE" })).Count);
            }
        }

        [TestMethod]
        public async Task Query_ErrorsOnlyIgnoresTheCategoryChip()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            using (ActivityHistoryService service = new ActivityHistoryService(null, directory.File("activity.jsonl")))
            {
                await service.RecordAsync(Record(ActivityAction.Connection, ActivityResult.Failed, "storage", size: null));

                IReadOnlyList<ActivityRecord> errors = await service.QueryAsync(
                    new ActivityQuery { ErrorsOnly = true, Category = ActivityCategory.Uploads });

                Assert.AreEqual(1, errors.Count);
            }
        }

        [TestMethod]
        public async Task Summarize_CountsOutcomesAndOnlyTransferBytes()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            using (ActivityHistoryService service = new ActivityHistoryService(null, directory.File("activity.jsonl")))
            {
                await service.RecordAsync(Record(ActivityAction.Upload, size: 1000));
                await service.RecordAsync(Record(ActivityAction.Download, size: 2000));
                await service.RecordAsync(Record(ActivityAction.Upload, ActivityResult.Failed, size: 4000));

                // A delete reports a size but moved no bytes over the network.
                await service.RecordAsync(Record(ActivityAction.Delete, size: 8000));

                ActivitySummary summary = await service.SummarizeAsync(new ActivityQuery());

                Assert.AreEqual(4, summary.EventCount);
                Assert.AreEqual(3, summary.SucceededCount);
                Assert.AreEqual(1, summary.FailedCount);
                Assert.AreEqual(7000, summary.TransferredBytes);
            }
        }

        [TestMethod]
        public async Task Prune_DropsEntriesOlderThanTheRetentionWindow()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            using (ActivityHistoryService service = new ActivityHistoryService(null, directory.File("activity.jsonl")))
            {
                DateTime now = DateTime.UtcNow;
                await service.RecordAsync(Record(ActivityAction.Upload, timestampUtc: now));
                await service.RecordAsync(Record(ActivityAction.Upload, timestampUtc: now.AddDays(-31)));
                await service.RecordAsync(Record(ActivityAction.Upload, timestampUtc: now.AddDays(-400)));

                int removed = await service.PruneAsync(30);

                Assert.AreEqual(2, removed);
                Assert.AreEqual(1, (await service.QueryAsync(new ActivityQuery())).Count);
            }
        }

        [TestMethod]
        public async Task Prune_ClampsAnAbsurdRetentionValue()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            using (ActivityHistoryService service = new ActivityHistoryService(null, directory.File("activity.jsonl")))
            {
                await service.RecordAsync(Record(ActivityAction.Upload, timestampUtc: DateTime.UtcNow.AddDays(-2)));

                Assert.AreEqual(1, await service.PruneAsync(0), "0 clamps to the one-day minimum");
                Assert.AreEqual(0, (await service.QueryAsync(new ActivityQuery())).Count);
            }
        }

        [TestMethod]
        public async Task Read_SkipsCorruptLinesInsteadOfLosingTheWholeFile()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string path = directory.File("activity.jsonl");

                using (ActivityHistoryService writer = new ActivityHistoryService(null, path))
                {
                    await writer.RecordAsync(Record(ActivityAction.Upload, target: "assets/good-one.zip"));
                    await writer.RecordAsync(Record(ActivityAction.Upload, target: "assets/good-two.zip"));
                }

                // Simulate a crash mid-append plus one entirely bogus line.
                File.AppendAllText(path, "{\"id\":\"broken\",\"timestampU" + Environment.NewLine, new UTF8Encoding(false));
                File.AppendAllText(path, "not json at all" + Environment.NewLine, new UTF8Encoding(false));

                using (ActivityHistoryService reader = new ActivityHistoryService(null, path))
                {
                    IReadOnlyList<ActivityRecord> all = await reader.QueryAsync(new ActivityQuery());
                    Assert.AreEqual(2, all.Count);
                }
            }
        }

        [TestMethod]
        public async Task Clear_RemovesEverythingAndRaisesCleared()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            using (ActivityHistoryService service = new ActivityHistoryService(null, directory.File("activity.jsonl")))
            {
                bool cleared = false;
                service.Cleared += (sender, args) => cleared = true;

                await service.RecordAsync(Record(ActivityAction.Upload));
                Assert.IsTrue(await service.ClearAsync());

                Assert.IsTrue(cleared);
                Assert.AreEqual(0, (await service.QueryAsync(new ActivityQuery())).Count);
                Assert.IsFalse(File.Exists(service.FilePath));
            }
        }

        // Break caught: reporting a clear as successful when the delete failed makes the UI lie about retained history.
        [TestMethod]
        public async Task Clear_WhenDeleteFails_ReturnsFalseAndDoesNotRaiseCleared()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            using (ActivityHistoryService service = new ActivityHistoryService(
                null, directory.File("activity.jsonl"), AtomicFileWriter.Shared, new ThrowingFileDeleter()))
            {
                await service.RecordAsync(Record(ActivityAction.Upload));
                bool raised = false;
                service.Cleared += (sender, args) => raised = true;

                bool result = await service.ClearAsync();

                Assert.IsFalse(result);
                Assert.IsFalse(raised);
                Assert.IsTrue(File.Exists(service.FilePath));
            }
        }

        // Break caught: a suppressed access/probe failure must not report a retained history file as cleared.
        [TestMethod]
        public async Task Clear_WhenAbsenceProbeFails_ReturnsFalseAndDoesNotRaiseCleared()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            using (ActivityHistoryService service = new ActivityHistoryService(
                null,
                directory.File("activity.jsonl"),
                AtomicFileWriter.Shared,
                new FileSystemActivityHistoryFileDeleter(),
                new ThrowingFileProbe()))
            {
                await service.RecordAsync(Record(ActivityAction.Upload));
                bool raised = false;
                service.Cleared += (sender, args) => raised = true;

                bool result = await service.ClearAsync();

                Assert.IsFalse(result);
                Assert.IsFalse(raised);
                Assert.IsFalse(File.Exists(service.FilePath));
            }
        }

        // Break caught: a failed rewrite after pruning must leave the previously persisted JSON Lines bytes intact.
        [TestMethod]
        public async Task Prune_WhenAtomicReplacementFails_ReturnsZeroAndPreservesExistingBytes()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            using (ActivityHistoryService service = new ActivityHistoryService(
                null,
                directory.File("activity.jsonl"),
                new AtomicFileWriter(new ThrowingCommitter())))
            {
                await service.RecordAsync(Record(ActivityAction.Upload, timestampUtc: DateTime.UtcNow.AddDays(-31)));
                await service.RecordAsync(Record(ActivityAction.Upload, timestampUtc: DateTime.UtcNow));
                byte[] before = File.ReadAllBytes(service.FilePath);

                Assert.AreEqual(0, await service.PruneAsync(30));

                CollectionAssert.AreEqual(before, File.ReadAllBytes(service.FilePath));
                Assert.AreEqual(0, Directory.GetFiles(directory.Path, "*.tmp").Length);
            }
        }

        [TestMethod]
        public async Task Record_KeepsTheFileBoundedWhenManyEntriesAreWritten()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            using (ActivityHistoryService service = new ActivityHistoryService(null, directory.File("activity.jsonl")))
            {
                string longTarget = "assets/builds/" + new string('k', 400) + ".bin";
                for (int index = 0; index < 1200; index++)
                {
                    await service.RecordAsync(Record(ActivityAction.Upload, target: longTarget + index));
                }

                Assert.IsTrue(
                    new FileInfo(service.FilePath).Length <= ActivityHistoryService.MaxFileBytes,
                    "the history must stay inside its size bound");
                Assert.IsTrue((await service.QueryAsync(new ActivityQuery())).Count > 0);
            }
        }

        // -------------------------------------------------------------------- sanitisation

        [TestMethod]
        public async Task Record_StripsTheQueryStringFromASignedUrl()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            using (ActivityHistoryService service = new ActivityHistoryService(null, directory.File("activity.jsonl")))
            {
                await service.RecordAsync(new ActivityRecord
                {
                    Action = ActivityAction.TemporaryLink,
                    Result = ActivityResult.Created,
                    Target = "https://acct.r2.cloudflarestorage.com/storage/assets/app.zip" +
                             "?X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential=AKIAEXAMPLE%2F20260804" +
                             "&X-Amz-Signature=1f3d9c0b7a2e4f5061728394a5b6c7d8e9f0a1b2c3d4e5f60718293a4b5c6d7e"
                });

                ActivityRecord stored = (await service.QueryAsync(new ActivityQuery()))[0];

                Assert.AreEqual(
                    "https://acct.r2.cloudflarestorage.com/storage/assets/app.zip",
                    stored.Target);
                Assert.IsFalse(stored.Target.Contains("X-Amz-Signature", StringComparison.OrdinalIgnoreCase));

                string raw = File.ReadAllText(service.FilePath);
                Assert.IsFalse(raw.Contains("X-Amz-Signature", StringComparison.OrdinalIgnoreCase));
                Assert.IsFalse(raw.Contains("X-Amz-Credential", StringComparison.OrdinalIgnoreCase));
            }
        }

        [TestMethod]
        public void Sanitize_RedactsCredentialShapedErrorText()
        {
            ActivityRecord sanitized = ActivitySanitizer.Sanitize(new ActivityRecord
            {
                Action = ActivityAction.Connection,
                Result = ActivityResult.Failed,
                ErrorMessage =
                    "Authorization: AWS4-HMAC-SHA256 Credential=AKIAEXAMPLE, " +
                    "secret_access_key=1f3d9c0b7a2e4f5061728394a5b6c7d8e9f0a1b2c3d4e5f60718293a4b5c6d7e"
            });

            Assert.IsFalse(sanitized.ErrorMessage.Contains("AKIAEXAMPLE", StringComparison.Ordinal));
            Assert.IsFalse(
                sanitized.ErrorMessage.Contains(
                    "1f3d9c0b7a2e4f5061728394a5b6c7d8e9f0a1b2c3d4e5f60718293a4b5c6d7e", StringComparison.Ordinal));
            StringAssert.Contains(sanitized.ErrorMessage, "redacted");
        }

        [TestMethod]
        public void Sanitize_RemovesControlCharactersSoARecordCannotForgeExtraLines()
        {
            ActivityRecord sanitized = ActivitySanitizer.Sanitize(new ActivityRecord
            {
                Target = "assets/one.zip\r\n{\"id\":\"forged\"}",
                ErrorMessage = "line one\nline two"
            });

            Assert.IsFalse(sanitized.Target.Contains('\n'));
            Assert.IsFalse(sanitized.Target.Contains('\r'));
            Assert.IsFalse(sanitized.ErrorMessage.Contains('\n'));
        }

        [TestMethod]
        public void Sanitize_BoundsEveryFieldLength()
        {
            ActivityRecord sanitized = ActivitySanitizer.Sanitize(new ActivityRecord
            {
                Target = new string('a', 5000),
                SecondaryTarget = new string('b', 5000),
                ErrorMessage = new string('c', 5000),
                ErrorCode = new string('d', 5000)
            });

            Assert.IsTrue(sanitized.Target.Length <= ActivitySanitizer.MaxTargetLength);
            Assert.IsTrue(sanitized.SecondaryTarget.Length <= ActivitySanitizer.MaxTargetLength);
            Assert.IsTrue(sanitized.ErrorMessage.Length <= ActivitySanitizer.MaxMessageLength);
            Assert.IsTrue(sanitized.ErrorCode.Length <= ActivitySanitizer.MaxCodeLength);
        }

        [TestMethod]
        public void Sanitize_DoesNotMutateTheCallersRecord()
        {
            ActivityRecord original = new ActivityRecord { Target = "https://cdn.example.com/a.zip?token=secret" };

            ActivitySanitizer.Sanitize(original);

            StringAssert.Contains(original.Target, "token=secret");
        }

        [TestMethod]
        public void Category_MapsEveryActionToAChip()
        {
            Assert.AreEqual(ActivityCategory.Uploads, new ActivityRecord { Action = ActivityAction.Upload }.Category);
            Assert.AreEqual(ActivityCategory.Downloads, new ActivityRecord { Action = ActivityAction.Download }.Category);
            Assert.AreEqual(ActivityCategory.Changes, new ActivityRecord { Action = ActivityAction.Delete }.Category);
            Assert.AreEqual(ActivityCategory.Changes, new ActivityRecord { Action = ActivityAction.Rename }.Category);
            Assert.AreEqual(ActivityCategory.Changes, new ActivityRecord { Action = ActivityAction.Move }.Category);
            Assert.AreEqual(ActivityCategory.Changes, new ActivityRecord { Action = ActivityAction.NewFolder }.Category);
            Assert.AreEqual(ActivityCategory.Other, new ActivityRecord { Action = ActivityAction.PublicLink }.Category);
            Assert.AreEqual(ActivityCategory.Other, new ActivityRecord { Action = ActivityAction.Connection }.Category);
        }

        [TestMethod]
        public void UnknownPersistedEnumValuesFallBackInsteadOfThrowing()
        {
            ActivityRecord record = new ActivityRecord { ActionValue = 9999, ResultValue = 9999 };

            Assert.AreEqual(ActivityAction.Upload, record.Action);
            Assert.AreEqual(ActivityResult.Success, record.Result);
            Assert.IsFalse(record.IsStructurallyValid());
        }

        private sealed class ThrowingCommitter : IAtomicFileCommitter
        {
            public void Commit(string temporaryPath, string destinationPath) =>
                throw new IOException("synthetic replacement failure");
        }

        private sealed class ThrowingFileDeleter : IActivityHistoryFileDeleter
        {
            public void Delete(string path) => throw new IOException("synthetic delete failure");
        }

        private sealed class ThrowingFileProbe : IActivityHistoryFileProbe
        {
            public void Probe(string path) => throw new UnauthorizedAccessException("synthetic probe failure");
        }
    }
}
