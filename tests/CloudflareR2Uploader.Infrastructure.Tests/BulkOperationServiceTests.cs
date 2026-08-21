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
    public sealed class BulkOperationServiceTests
    {
        private static readonly string[] ExpectedObjectKeys = { "outside.txt", "root/", "root/a.txt", "root/nested/b.txt" };
        private static readonly string[] ExpectedEmptyDirectoryPaths = { "empty" };

        [TestMethod] public async Task Plan_DeduplicatesOverlapsFollowsPaginationAndIncludesMarkers()
        {
            FakeSource source = new FakeSource();
            source.Pages["root/|"] = Page("next", Entry("root/", 0, true), Entry("root/a.txt", 10, false));
            source.Pages["root/|next"] = Page(null, Entry("root/nested/b.txt", 20, false));
            R2BulkOperationService service = new R2BulkOperationService(null, source);
            R2BrowserItem[] selected = {
                new R2BrowserItem { IsFolder=true, Prefix="root/", DisplayName="root" },
                new R2BrowserItem { IsFolder=true, Prefix="root/nested/", DisplayName="nested" },
                new R2BrowserItem { Key="root/a.txt", DisplayName="a.txt", Size=10 },
                new R2BrowserItem { Key="outside.txt", DisplayName="outside.txt", Size=5 }
            };
            BulkObjectOperationPlan plan = await service.PlanAsync(Settings(), Credentials(), selected, null, CancellationToken.None);
            CollectionAssert.AreEqual(ExpectedObjectKeys, plan.Objects.Select(x => x.Key).ToArray());
            Assert.AreEqual(35, plan.TotalBytes); Assert.AreEqual(2, source.ListCalls); Assert.IsTrue(plan.Objects.Single(x => x.Key == "root/").IsFolderMarker);
        }
        [TestMethod] public async Task Plan_RecordsSelectedEmptyFolderAndHonorsCancellation()
        {
            FakeSource source = new FakeSource(); source.Pages["empty/|"] = Page(null);
            R2BulkOperationService service = new R2BulkOperationService(null, source);
            BulkObjectOperationPlan plan = await service.PlanAsync(Settings(), Credentials(), new[] { new R2BrowserItem { IsFolder=true, Prefix="empty/" } }, null, CancellationToken.None);
            CollectionAssert.AreEqual(ExpectedEmptyDirectoryPaths, plan.EmptyDirectoryPaths.ToArray());
            CancellationTokenSource cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => service.PlanAsync(Settings(), Credentials(), new[] { new R2BrowserItem { IsFolder=true, Prefix="empty/" } }, null, cancellation.Token));
        }

        [TestMethod]
        public async Task Download_RenameAutomatically_ReservesPlannedPathsAcrossOverlappingWorkers()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            using (Barrier barrier = new Barrier(2))
            {
                string report = directory.File("report.txt");
                string numberedReport = directory.File("report (1).txt");
                File.WriteAllText(report, "existing");

                OverlappingDownloadSource source = new OverlappingDownloadSource(barrier);
                R2BulkOperationService service = new R2BulkOperationService(null, source);
                BulkObjectOperationPlan plan = new BulkObjectOperationPlan { DestinationDirectory = directory.Path };
                plan.Objects.Add(new BulkObjectEntry { Key = "first", Size = 5, LocalPath = report });
                plan.Objects.Add(new BulkObjectEntry { Key = "second", Size = 6, LocalPath = numberedReport });

                BulkObjectOperationResult result = await service.DownloadAsync(
                    Settings(), Credentials(), plan, BulkConflictBehavior.RenameAutomatically,
                    null, null, CancellationToken.None);

                Assert.AreEqual(2, result.Succeeded);
                Assert.AreEqual("second", File.ReadAllText(numberedReport));
                Assert.AreEqual("first", File.ReadAllText(directory.File("report (2).txt")));
                Assert.AreEqual(0, Directory.GetFiles(directory.Path, "*.r2partial").Length);
            }
        }

        [TestMethod]
        public async Task Download_CaseVariantPlannedPaths_RenamesLaterOwnerWithoutCorruption()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            using (Barrier barrier = new Barrier(2))
            {
                string firstPath = directory.File("Report.txt");
                string aliasPath = directory.File("report.txt");
                string renamedPath = directory.File("report (1).txt");
                OverlappingDownloadSource source = new OverlappingDownloadSource(barrier);
                R2BulkOperationService service = new R2BulkOperationService(null, source);
                BulkObjectOperationPlan plan = Plan(
                    new BulkObjectEntry { Key = "first", Size = 5, LocalPath = firstPath },
                    new BulkObjectEntry { Key = "second", Size = 6, LocalPath = aliasPath });

                BulkObjectOperationResult result = await service.DownloadAsync(
                    Settings(), Credentials(), plan, BulkConflictBehavior.RenameAutomatically,
                    null, null, CancellationToken.None);

                Assert.AreEqual(2, result.Succeeded);
                Assert.AreEqual(1, result.Renamed);
                Assert.AreEqual(0, result.Skipped);
                Assert.AreEqual(0, result.Failures.Count);
                Assert.AreEqual("first", File.ReadAllText(firstPath));
                Assert.IsTrue(File.Exists(renamedPath));
                Assert.AreEqual("second", File.ReadAllText(renamedPath));
                Assert.AreEqual(2, Directory.GetFiles(directory.Path).Length);
                Assert.AreEqual(0, Directory.GetFiles(directory.Path, "*.r2partial").Length);
            }
        }

        [TestMethod]
        public async Task Download_FinalToPartialPlannedAlias_RenamesLaterOwnerWithoutCorruption()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            using (Barrier barrier = new Barrier(2))
            {
                string firstPath = directory.File("foo");
                string aliasPath = directory.File("foo.r2partial");
                string renamedPath = directory.File("foo (1).r2partial");
                OverlappingDownloadSource source = new OverlappingDownloadSource(barrier);
                R2BulkOperationService service = new R2BulkOperationService(null, source);
                BulkObjectOperationPlan plan = Plan(
                    new BulkObjectEntry { Key = "first", Size = 5, LocalPath = firstPath },
                    new BulkObjectEntry { Key = "second", Size = 6, LocalPath = aliasPath });

                BulkObjectOperationResult result = await service.DownloadAsync(
                    Settings(), Credentials(), plan, BulkConflictBehavior.RenameAutomatically,
                    null, null, CancellationToken.None);

                Assert.AreEqual(2, result.Succeeded);
                Assert.AreEqual(1, result.Renamed);
                Assert.AreEqual(0, result.Skipped);
                Assert.AreEqual(0, result.Failures.Count);
                Assert.AreEqual("first", File.ReadAllText(firstPath));
                Assert.IsFalse(File.Exists(aliasPath));
                Assert.IsTrue(File.Exists(renamedPath));
                Assert.AreEqual("second", File.ReadAllText(renamedPath));
                Assert.IsFalse(File.Exists(renamedPath + ".r2partial"));
                Assert.AreEqual(2, Directory.GetFiles(directory.Path).Length);
            }
        }

        [TestMethod]
        public async Task Download_CaseVariantPlannedPaths_SkipsLaterOwner()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            using (Barrier barrier = new Barrier(1))
            {
                string firstPath = directory.File("Report.txt");
                OverlappingDownloadSource source = new OverlappingDownloadSource(barrier);
                R2BulkOperationService service = new R2BulkOperationService(null, source);
                BulkObjectOperationPlan plan = Plan(
                    new BulkObjectEntry { Key = "first", Size = 5, LocalPath = firstPath },
                    new BulkObjectEntry { Key = "second", Size = 6, LocalPath = directory.File("report.txt") });

                BulkObjectOperationResult result = await service.DownloadAsync(
                    Settings(), Credentials(), plan, BulkConflictBehavior.Skip,
                    null, null, CancellationToken.None);

                Assert.AreEqual(2, result.Completed);
                Assert.AreEqual(1, result.Succeeded);
                Assert.AreEqual(1, result.Skipped);
                Assert.AreEqual(0, result.Renamed);
                Assert.AreEqual(0, result.Failures.Count);
                Assert.AreEqual("first", File.ReadAllText(firstPath));
                Assert.AreEqual(1, Directory.GetFiles(directory.Path).Length);
            }
        }

        [TestMethod]
        public async Task Download_ExternalFinalAppearsAfterClaim_AutomaticRenamePreservesBothFiles()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string target = directory.File("report.txt");
                string renamed = directory.File("report (1).txt");
                MaterializingDownloadSource source = new MaterializingDownloadSource(target, "external");
                R2BulkOperationService service = new R2BulkOperationService(null, source);
                BulkObjectOperationPlan plan = Plan(
                    new BulkObjectEntry { Key = "download", Size = 8, LocalPath = target });

                BulkObjectOperationResult result = await service.DownloadAsync(
                    Settings(), Credentials(), plan, BulkConflictBehavior.RenameAutomatically,
                    null, null, CancellationToken.None);

                Assert.AreEqual(1, result.Succeeded);
                Assert.AreEqual(1, result.Renamed);
                Assert.AreEqual(0, result.Skipped);
                Assert.AreEqual(0, result.Failures.Count);
                Assert.AreEqual("external", File.ReadAllText(target));
                Assert.AreEqual("download", File.ReadAllText(renamed));
                Assert.AreEqual(0, Directory.GetFiles(directory.Path, "*.r2partial").Length);
            }
        }

        [TestMethod]
        public async Task Download_ExternalFinalAppearsAfterClaim_SkipPreservesExternalFile()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string target = directory.File("report.txt");
                MaterializingDownloadSource source = new MaterializingDownloadSource(target, "external");
                R2BulkOperationService service = new R2BulkOperationService(null, source);
                BulkObjectOperationPlan plan = Plan(
                    new BulkObjectEntry { Key = "download", Size = 8, LocalPath = target });

                BulkObjectOperationResult result = await service.DownloadAsync(
                    Settings(), Credentials(), plan, BulkConflictBehavior.Skip,
                    null, null, CancellationToken.None);

                Assert.AreEqual(1, result.Completed);
                Assert.AreEqual(0, result.Succeeded);
                Assert.AreEqual(1, result.Skipped);
                Assert.AreEqual(0, result.Failures.Count);
                Assert.AreEqual("external", File.ReadAllText(target));
                Assert.AreEqual(0, Directory.GetFiles(directory.Path, "*.r2partial").Length);
            }
        }

        [TestMethod]
        public async Task Download_ExternalFinalAppearsAfterClaim_AskRenamePreservesBothFiles()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string target = directory.File("report.txt");
                string renamed = directory.File("report (1).txt");
                MaterializingDownloadSource source = new MaterializingDownloadSource(target, "external");
                R2BulkOperationService service = new R2BulkOperationService(null, source);
                BulkObjectOperationPlan plan = Plan(
                    new BulkObjectEntry { Key = "download", Size = 8, LocalPath = target });

                BulkObjectOperationResult result = await service.DownloadAsync(
                    Settings(), Credentials(), plan, BulkConflictBehavior.Ask,
                    path => Task.FromResult(new BulkConflictDecision { Behavior = BulkConflictBehavior.RenameAutomatically }),
                    null, CancellationToken.None);

                Assert.AreEqual(1, result.Succeeded);
                Assert.AreEqual(1, result.Renamed);
                Assert.AreEqual(0, result.Skipped);
                Assert.AreEqual(0, result.Failures.Count);
                Assert.AreEqual("external", File.ReadAllText(target));
                Assert.AreEqual("download", File.ReadAllText(renamed));
                Assert.AreEqual(0, Directory.GetFiles(directory.Path, "*.r2partial").Length);
            }
        }

        [TestMethod]
        public async Task Download_ExternalPartialAppearsBeforeOpen_FailureDoesNotDeleteIt()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string target = directory.File("report.txt");
                string partial = target + ".r2partial";
                File.WriteAllText(target, "existing");
                using (Barrier barrier = new Barrier(1))
                {
                    OverlappingDownloadSource source = new OverlappingDownloadSource(barrier);
                    R2BulkOperationService service = new R2BulkOperationService(null, source);
                    BulkObjectOperationPlan plan = Plan(
                        new BulkObjectEntry { Key = "download", Size = 8, LocalPath = target });

                    BulkObjectOperationResult result = await service.DownloadAsync(
                        Settings(), Credentials(), plan, BulkConflictBehavior.Ask,
                        path =>
                        {
                            File.WriteAllText(partial, "external-partial");
                            return Task.FromResult(new BulkConflictDecision { Behavior = BulkConflictBehavior.Overwrite });
                        },
                        null, CancellationToken.None);

                    Assert.AreEqual(1, result.Completed);
                    Assert.AreEqual(0, result.Succeeded);
                    Assert.AreEqual(0, result.Skipped);
                    Assert.AreEqual(1, result.Failures.Count);
                    Assert.AreEqual("existing", File.ReadAllText(target));
                    Assert.IsTrue(File.Exists(partial));
                    Assert.AreEqual("external-partial", File.ReadAllText(partial));
                }
            }
        }

        [TestMethod]
        public async Task Download_PartialPathReplacedAfterWrite_FinalUsesOwnedBytesAndPreservesReplacement()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string target = directory.File("report.txt");
                string renamed = directory.File("report (1).txt");
                string partial = target + ".r2partial";
                string displacedOwnedPartial = partial + ".displaced";
                MaterializingDownloadSource source = new MaterializingDownloadSource(target, "external-final");
                R2BulkOperationService service = new R2BulkOperationService(null, source);
                BulkObjectOperationPlan plan = Plan(
                    new BulkObjectEntry { Key = "download", Size = 8, LocalPath = target });

                BulkObjectOperationResult result = await service.DownloadAsync(
                    Settings(), Credentials(), plan, BulkConflictBehavior.Ask,
                    path =>
                    {
                        if (File.Exists(partial)) File.Move(partial, displacedOwnedPartial);
                        File.WriteAllText(partial, "external-partial");
                        return Task.FromResult(new BulkConflictDecision { Behavior = BulkConflictBehavior.RenameAutomatically });
                    },
                    null, CancellationToken.None);

                Assert.AreEqual(1, result.Completed);
                Assert.AreEqual(1, result.Succeeded, result.Failures.Count == 0 ? null : result.Failures[0].Message);
                Assert.AreEqual(1, result.Renamed);
                Assert.AreEqual(0, result.Skipped);
                Assert.AreEqual(0, result.Failures.Count);
                Assert.AreEqual(8, result.TransferredBytes);
                Assert.AreEqual("external-final", File.ReadAllText(target));
                Assert.AreEqual("download", File.ReadAllText(renamed));
                Assert.IsTrue(File.Exists(partial), "The external replacement partial must survive; displaced owned object exists=" + File.Exists(displacedOwnedPartial));
                Assert.AreEqual("external-partial", File.ReadAllText(partial));
                Assert.IsFalse(File.Exists(displacedOwnedPartial));
            }
        }

        [TestMethod]
        public async Task Download_NewDestination_MaterializationFailureLeavesNoFinalOrCommitTemp()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            using (Barrier barrier = new Barrier(1))
            {
                string target = directory.File("report.txt");
                R2BulkOperationService service = new R2BulkOperationService(
                    null, new OverlappingDownloadSource(barrier),
                    (stage, cancellationToken) => Task.FromException(new IOException("Injected materialization failure.")));
                BulkObjectOperationPlan plan = Plan(
                    new BulkObjectEntry { Key = "download", Size = 8, LocalPath = target });

                BulkObjectOperationResult result = await service.DownloadAsync(
                    Settings(), Credentials(), plan, BulkConflictBehavior.RenameAutomatically,
                    null, null, CancellationToken.None);

                Assert.AreEqual(1, result.Completed);
                Assert.AreEqual(0, result.Succeeded);
                Assert.AreEqual(0, result.Skipped);
                Assert.AreEqual(1, result.Failures.Count);
                Assert.AreEqual(0, result.TransferredBytes);
                Assert.IsFalse(File.Exists(target));
                Assert.AreEqual(0, CommitFileCount(directory.Path));
            }
        }

        [TestMethod]
        public async Task Download_Overwrite_MaterializationFailurePreservesExistingFinal()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            using (Barrier barrier = new Barrier(1))
            {
                string target = directory.File("report.txt");
                File.WriteAllText(target, "existing-final");
                R2BulkOperationService service = new R2BulkOperationService(
                    null, new OverlappingDownloadSource(barrier),
                    (stage, cancellationToken) => Task.FromException(new IOException("Injected materialization failure.")));
                BulkObjectOperationPlan plan = Plan(
                    new BulkObjectEntry { Key = "download", Size = 8, LocalPath = target });

                BulkObjectOperationResult result = await service.DownloadAsync(
                    Settings(), Credentials(), plan, BulkConflictBehavior.Overwrite,
                    null, null, CancellationToken.None);

                Assert.AreEqual(1, result.Completed);
                Assert.AreEqual(0, result.Succeeded);
                Assert.AreEqual(0, result.Skipped);
                Assert.AreEqual(1, result.Failures.Count);
                Assert.AreEqual(0, result.TransferredBytes);
                Assert.AreEqual("existing-final", File.ReadAllText(target));
                Assert.AreEqual(0, CommitFileCount(directory.Path));
            }
        }

        [TestMethod]
        public async Task Download_Overwrite_AtomicallyReplacesExistingFinalAndCountsSuccess()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            using (Barrier barrier = new Barrier(1))
            {
                string target = directory.File("report.txt");
                File.WriteAllText(target, "existing-final");
                R2BulkOperationService service = new R2BulkOperationService(
                    null, new OverlappingDownloadSource(barrier), null,
                    targetPath => new TestDownloadCommit(targetPath));
                BulkObjectOperationPlan plan = Plan(
                    new BulkObjectEntry { Key = "download", Size = 8, LocalPath = target });

                BulkObjectOperationResult result = await service.DownloadAsync(
                    Settings(), Credentials(), plan, BulkConflictBehavior.Overwrite,
                    null, null, CancellationToken.None);

                Assert.AreEqual(1, result.Completed);
                Assert.AreEqual(1, result.Succeeded);
                Assert.AreEqual(0, result.Skipped);
                Assert.AreEqual(0, result.Failures.Count);
                Assert.AreEqual(8, result.TransferredBytes);
                Assert.AreEqual("download", File.ReadAllText(target));
                Assert.AreEqual(0, CommitFileCount(directory.Path));
                Assert.AreEqual(0, Directory.GetFiles(directory.Path, "*.r2partial").Length);
            }
        }

        [TestMethod]
        public async Task Download_MaterializationCancellationPreservesExternalReplacementsAndLeavesNoOrphans()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            using (Barrier barrier = new Barrier(1))
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                string target = directory.File("report.txt");
                string partial = target + ".r2partial";
                string displacedFinal = target + ".displaced";
                string displacedPartial = partial + ".displaced";
                R2BulkOperationService service = new R2BulkOperationService(
                    null, new OverlappingDownloadSource(barrier),
                    (stage, cancellationToken) =>
                    {
                        if (File.Exists(target)) File.Move(target, displacedFinal);
                        if (File.Exists(partial)) File.Move(partial, displacedPartial);
                        File.WriteAllText(target, "external-final");
                        File.WriteAllText(partial, "external-partial");
                        cancellation.Cancel();
                        return Task.FromCanceled(cancellationToken);
                    });
                BulkObjectOperationPlan plan = Plan(
                    new BulkObjectEntry { Key = "download", Size = 8, LocalPath = target });

                BulkObjectOperationResult result = await service.DownloadAsync(
                    Settings(), Credentials(), plan, BulkConflictBehavior.RenameAutomatically,
                    null, null, cancellation.Token);

                Assert.IsTrue(result.Cancelled);
                Assert.AreEqual(0, result.Completed);
                Assert.AreEqual(0, result.Succeeded);
                Assert.AreEqual(0, result.Failures.Count);
                Assert.AreEqual(0, result.TransferredBytes);
                Assert.AreEqual("external-final", File.ReadAllText(target));
                Assert.AreEqual("external-partial", File.ReadAllText(partial));
                Assert.IsFalse(File.Exists(displacedFinal));
                Assert.IsFalse(File.Exists(displacedPartial));
                Assert.AreEqual(0, CommitFileCount(directory.Path));
            }
        }

        [DataTestMethod]
        [DataRow((int)DownloadCommitStage.Copied, false)]
        [DataRow((int)DownloadCommitStage.AsyncFlushed, false)]
        [DataRow((int)DownloadCommitStage.LengthValidated, false)]
        [DataRow((int)DownloadCommitStage.TimestampApplied, false)]
        [DataRow((int)DownloadCommitStage.DurablyFlushed, false)]
        [DataRow((int)DownloadCommitStage.Copied, true)]
        [DataRow((int)DownloadCommitStage.AsyncFlushed, true)]
        [DataRow((int)DownloadCommitStage.LengthValidated, true)]
        [DataRow((int)DownloadCommitStage.TimestampApplied, true)]
        [DataRow((int)DownloadCommitStage.DurablyFlushed, true)]
        public async Task Download_MaterializationFailureAtStage_PreservesTarget(
            int injectedStageValue,
            bool overwrite)
        {
            DownloadCommitStage injectedStage = (DownloadCommitStage)injectedStageValue;
            using (TemporaryDirectory directory = new TemporaryDirectory())
            using (Barrier barrier = new Barrier(1))
            {
                string target = directory.File("report.txt");
                if (overwrite) File.WriteAllText(target, "existing-final");
                R2BulkOperationService service = new R2BulkOperationService(
                    null, new OverlappingDownloadSource(barrier),
                    (stage, cancellationToken) => stage == injectedStage
                        ? Task.FromException(new IOException("Injected failure at " + stage + "."))
                        : Task.CompletedTask);
                BulkObjectOperationPlan plan = Plan(
                    new BulkObjectEntry
                    {
                        Key = "download",
                        Size = 8,
                        LocalPath = target,
                        LastModifiedUtc = new DateTime(2024, 3, 4, 5, 6, 7, DateTimeKind.Utc)
                    });

                BulkObjectOperationResult result = await service.DownloadAsync(
                    Settings(), Credentials(), plan,
                    overwrite ? BulkConflictBehavior.Overwrite : BulkConflictBehavior.RenameAutomatically,
                    null, null, CancellationToken.None);

                Assert.AreEqual(1, result.Completed);
                Assert.AreEqual(0, result.Succeeded);
                Assert.AreEqual(1, result.Failures.Count);
                Assert.AreEqual(0, result.TransferredBytes);
                if (overwrite)
                    Assert.AreEqual("existing-final", File.ReadAllText(target));
                else
                    Assert.IsFalse(File.Exists(target));
                Assert.AreEqual(0, CommitFileCount(directory.Path));
            }
        }

        [DataTestMethod]
        [DataRow((int)DownloadCommitStage.Copied, false)]
        [DataRow((int)DownloadCommitStage.AsyncFlushed, false)]
        [DataRow((int)DownloadCommitStage.LengthValidated, false)]
        [DataRow((int)DownloadCommitStage.TimestampApplied, false)]
        [DataRow((int)DownloadCommitStage.DurablyFlushed, false)]
        [DataRow((int)DownloadCommitStage.Copied, true)]
        [DataRow((int)DownloadCommitStage.AsyncFlushed, true)]
        [DataRow((int)DownloadCommitStage.LengthValidated, true)]
        [DataRow((int)DownloadCommitStage.TimestampApplied, true)]
        [DataRow((int)DownloadCommitStage.DurablyFlushed, true)]
        public async Task Download_MaterializationCancellationAtStage_PreservesTarget(
            int injectedStageValue,
            bool overwrite)
        {
            DownloadCommitStage injectedStage = (DownloadCommitStage)injectedStageValue;
            using (TemporaryDirectory directory = new TemporaryDirectory())
            using (Barrier barrier = new Barrier(1))
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                string target = directory.File("report.txt");
                if (overwrite) File.WriteAllText(target, "existing-final");
                R2BulkOperationService service = new R2BulkOperationService(
                    null, new OverlappingDownloadSource(barrier),
                    (stage, cancellationToken) =>
                    {
                        if (stage != injectedStage) return Task.CompletedTask;
                        cancellation.Cancel();
                        return Task.FromCanceled(cancellationToken);
                    });
                BulkObjectOperationPlan plan = Plan(
                    new BulkObjectEntry
                    {
                        Key = "download",
                        Size = 8,
                        LocalPath = target,
                        LastModifiedUtc = new DateTime(2024, 3, 4, 5, 6, 7, DateTimeKind.Utc)
                    });

                BulkObjectOperationResult result = await service.DownloadAsync(
                    Settings(), Credentials(), plan,
                    overwrite ? BulkConflictBehavior.Overwrite : BulkConflictBehavior.RenameAutomatically,
                    null, null, cancellation.Token);

                Assert.IsTrue(result.Cancelled);
                Assert.AreEqual(0, result.Completed);
                Assert.AreEqual(0, result.Succeeded);
                Assert.AreEqual(0, result.Failures.Count);
                Assert.AreEqual(0, result.TransferredBytes);
                if (overwrite)
                    Assert.AreEqual("existing-final", File.ReadAllText(target));
                else
                    Assert.IsFalse(File.Exists(target));
                Assert.AreEqual(0, CommitFileCount(directory.Path));
            }
        }

        [TestMethod]
        public async Task Download_CancellationWithCommitCloseFailure_RemainsCancelledAndLogsCleanup()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            using (Barrier barrier = new Barrier(1))
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                string target = directory.File("report.txt");
                IOException closeFailure = new IOException("injected close failure");
                RecordingLog log = new RecordingLog();
                R2BulkOperationService service = new R2BulkOperationService(
                    log, new OverlappingDownloadSource(barrier),
                    (stage, cancellationToken) =>
                    {
                        cancellation.Cancel();
                        return Task.FromCanceled(cancellationToken);
                    },
                    targetPath => CloseFailingCommit(targetPath, closeFailure));

                BulkObjectOperationResult result = await service.DownloadAsync(
                    Settings(), Credentials(),
                    Plan(new BulkObjectEntry { Key = "download", Size = 8, LocalPath = target }),
                    BulkConflictBehavior.RenameAutomatically,
                    null, null, cancellation.Token);

                Assert.IsTrue(result.Cancelled);
                Assert.AreEqual(0, result.Completed);
                Assert.AreEqual(0, result.Succeeded);
                Assert.AreEqual(0, result.Failures.Count);
                Assert.AreEqual(0, result.TransferredBytes);
                Assert.IsFalse(File.Exists(target));
                Assert.AreSame(closeFailure, log.Errors.Single());
            }
        }

        [TestMethod]
        public async Task Download_MaterializationFailureWithCommitCloseFailure_RetainsOriginalFailure()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            using (Barrier barrier = new Barrier(1))
            {
                string target = directory.File("report.txt");
                IOException primary = new IOException("original materialization failure");
                IOException closeFailure = new IOException("injected close failure");
                RecordingLog log = new RecordingLog();
                R2BulkOperationService service = new R2BulkOperationService(
                    log, new OverlappingDownloadSource(barrier),
                    (stage, cancellationToken) => Task.FromException(primary),
                    targetPath => CloseFailingCommit(targetPath, closeFailure));

                BulkObjectOperationResult result = await service.DownloadAsync(
                    Settings(), Credentials(),
                    Plan(new BulkObjectEntry { Key = "download", Size = 8, LocalPath = target }),
                    BulkConflictBehavior.RenameAutomatically,
                    null, null, CancellationToken.None);

                Assert.IsFalse(result.Cancelled);
                Assert.AreEqual(1, result.Completed);
                Assert.AreEqual(0, result.Succeeded);
                Assert.AreEqual(1, result.Failures.Count);
                Assert.AreEqual("original materialization failure", result.Failures[0].Message);
                Assert.AreEqual(0, result.TransferredBytes);
                Assert.IsFalse(File.Exists(target));
                Assert.AreSame(closeFailure, log.Errors.Single());
            }
        }

        [TestMethod]
        public async Task Download_SuccessWithCommitCloseFailure_RemainsSuccessfulAndLogsCleanup()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            using (Barrier barrier = new Barrier(1))
            {
                string target = directory.File("report.txt");
                IOException closeFailure = new IOException("injected close failure");
                RecordingLog log = new RecordingLog();
                R2BulkOperationService service = new R2BulkOperationService(
                    log, new OverlappingDownloadSource(barrier), null,
                    targetPath => CloseFailingCommit(targetPath, closeFailure));

                BulkObjectOperationResult result = await service.DownloadAsync(
                    Settings(), Credentials(),
                    Plan(new BulkObjectEntry { Key = "download", Size = 8, LocalPath = target }),
                    BulkConflictBehavior.RenameAutomatically,
                    null, null, CancellationToken.None);

                Assert.IsFalse(result.Cancelled);
                Assert.AreEqual(1, result.Completed);
                Assert.AreEqual(1, result.Succeeded);
                Assert.AreEqual(0, result.Failures.Count);
                Assert.AreEqual(8, result.TransferredBytes);
                Assert.AreEqual("download", File.ReadAllText(target));
                Assert.AreSame(closeFailure, log.Errors.Single());
            }
        }

        [TestMethod]
        public async Task Download_Success_PreservesRequestedTimestamp()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            using (Barrier barrier = new Barrier(1))
            {
                string target = directory.File("report.txt");
                DateTime timestamp = new DateTime(2024, 3, 4, 5, 6, 7, DateTimeKind.Utc);
                R2BulkOperationService service = new R2BulkOperationService(
                    null, new OverlappingDownloadSource(barrier));
                BulkObjectOperationPlan plan = Plan(
                    new BulkObjectEntry
                    {
                        Key = "download",
                        Size = 8,
                        LocalPath = target,
                        LastModifiedUtc = timestamp
                    });

                BulkObjectOperationResult result = await service.DownloadAsync(
                    Settings(), Credentials(), plan, BulkConflictBehavior.RenameAutomatically,
                    null, null, CancellationToken.None);

                Assert.IsFalse(result.Cancelled);
                Assert.AreEqual(1, result.Completed);
                Assert.AreEqual(1, result.Succeeded);
                Assert.AreEqual(0, result.Skipped);
                Assert.AreEqual(0, result.Failures.Count,
                    result.Failures.Count == 0 ? null : result.Failures[0].Message);
                Assert.AreEqual(8, result.TransferredBytes);
                Assert.IsTrue(File.Exists(target), "The successful result did not publish its target.");
                Assert.AreEqual("download", File.ReadAllText(target));
                Assert.AreEqual(timestamp, File.GetLastWriteTimeUtc(target));
            }
        }

        [TestMethod]
        public async Task Download_LinuxNearNameMax_PublishesExactPathWithDefaultPermissions()
        {
            if (!OperatingSystem.IsLinux()) return;
            using (TemporaryDirectory directory = new TemporaryDirectory())
            using (Barrier barrier = new Barrier(1))
            {
                string fileName = new string('x', 250);
                string target = directory.File(fileName);
                R2BulkOperationService service = new R2BulkOperationService(
                    null, new OverlappingDownloadSource(barrier));
                BulkObjectOperationPlan plan = Plan(
                    new BulkObjectEntry { Key = "download", Size = 8, LocalPath = target });

                BulkObjectOperationResult result = await service.DownloadAsync(
                    Settings(), Credentials(), plan, BulkConflictBehavior.RenameAutomatically,
                    null, null, CancellationToken.None);

                Assert.AreEqual(1, result.Succeeded);
                Assert.AreEqual("download", File.ReadAllText(target));
                UnixFileMode expectedMode = (UnixFileMode)(438 & ~ReadLinuxUmask());
                Assert.AreEqual(expectedMode, File.GetUnixFileMode(target));
                Assert.AreEqual(1, Directory.GetFiles(directory.Path).Length);
            }
        }

        private static int CommitFileCount(string directory)
        {
            return Directory.GetFiles(directory, "*.r2commit-*").Length +
                Directory.GetFiles(directory, ".r2c-*").Length;
        }

        private static int ReadLinuxUmask()
        {
            string line = File.ReadLines("/proc/self/status")
                .Single(value => value.StartsWith("Umask:", StringComparison.Ordinal));
            return Convert.ToInt32(line.Substring("Umask:".Length).Trim(), 8);
        }

        private static OwnedDownloadCommit CloseFailingCommit(string target, Exception closeFailure)
        {
            return OwnedDownloadCommit.Create(
                target,
                null,
                stream =>
                {
                    stream.Dispose();
                    throw closeFailure;
                });
        }

        private static BulkObjectOperationPlan Plan(params BulkObjectEntry[] entries)
        {
            BulkObjectOperationPlan plan = new BulkObjectOperationPlan();
            plan.Objects.AddRange(entries);
            return plan;
        }
        private static AppSettings Settings() { AppSettings settings = new AppSettings { CustomEndpoint="https://example.com", BucketName="bucket" }; settings.Clamp(); return settings; }
        private static R2Credentials Credentials() { return new R2Credentials("a", "b"); }
        private static BulkObjectEntry Entry(string key, long size, bool marker) { return new BulkObjectEntry { Key=key, Size=size, IsFolderMarker=marker }; }
        private static BulkListingPage Page(string token, params BulkObjectEntry[] entries) { BulkListingPage page = new BulkListingPage { NextContinuationToken=token }; page.Objects.AddRange(entries); return page; }
        private sealed class FakeSource : IR2BulkObjectSource
        {
            public readonly Dictionary<string,BulkListingPage> Pages = new Dictionary<string,BulkListingPage>(); public int ListCalls;
            public Task<BulkListingPage> ListAsync(AppSettings s,R2Credentials c,string p,string t,CancellationToken ct) { ct.ThrowIfCancellationRequested(); ListCalls++; return Task.FromResult(Pages[p+"|"+(t??"")]); }
            public Task<long?> DownloadAsync(AppSettings s,R2Credentials c,string k,Stream d,CancellationToken ct) { return Task.FromResult<long?>(0); }
            public Task<BulkDeleteBatchResult> DeleteAsync(AppSettings s,R2Credentials c,IList<string> k,CancellationToken ct) { return Task.FromResult(new BulkDeleteBatchResult()); }
        }

        private sealed class OverlappingDownloadSource : IR2BulkObjectSource
        {
            private readonly Barrier _barrier;

            public OverlappingDownloadSource(Barrier barrier)
            {
                _barrier = barrier;
            }

            public Task<BulkListingPage> ListAsync(AppSettings settings, R2Credentials credentials, string prefix, string continuationToken, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public async Task<long?> DownloadAsync(AppSettings settings, R2Credentials credentials, string key, Stream destination, CancellationToken cancellationToken)
            {
                if (!_barrier.SignalAndWait(TimeSpan.FromSeconds(5), cancellationToken))
                    throw new TimeoutException("Both download workers did not reach the transfer barrier.");

                byte[] content = System.Text.Encoding.UTF8.GetBytes(key);
                await destination.WriteAsync(content.AsMemory(), cancellationToken);
                return content.Length;
            }

            public Task<BulkDeleteBatchResult> DeleteAsync(AppSettings settings, R2Credentials credentials, IList<string> keys, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class MaterializingDownloadSource : IR2BulkObjectSource
        {
            private readonly string _materializedPath;
            private readonly string _materializedContent;

            public MaterializingDownloadSource(string materializedPath, string materializedContent)
            {
                _materializedPath = materializedPath;
                _materializedContent = materializedContent;
            }

            public Task<BulkListingPage> ListAsync(AppSettings settings, R2Credentials credentials, string prefix, string continuationToken, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public async Task<long?> DownloadAsync(AppSettings settings, R2Credentials credentials, string key, Stream destination, CancellationToken cancellationToken)
            {
                File.WriteAllText(_materializedPath, _materializedContent);
                byte[] content = System.Text.Encoding.UTF8.GetBytes(key);
                await destination.WriteAsync(content.AsMemory(), cancellationToken);
                return content.Length;
            }

            public Task<BulkDeleteBatchResult> DeleteAsync(AppSettings settings, R2Credentials credentials, IList<string> keys, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class TestDownloadCommit : IDownloadCommit
        {
            private readonly string _path;
            private readonly FileStream _stream;
            private bool _published;

            public TestDownloadCommit(string target)
            {
                string directory = Path.GetDirectoryName(target);
                _path = Path.Combine(directory, ".r2c-test-" + Guid.NewGuid().ToString("N"));
                _stream = new FileStream(
                    _path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Delete, 81920, true);
            }

            public FileStream Stream => _stream;
            public Exception CleanupFailure => null;

            public bool TryPublish(string target, bool overwrite)
            {
                _stream.Flush(true);
                _stream.Dispose();
                File.Move(_path, target, overwrite);
                _published = true;
                return true;
            }

            public void Dispose()
            {
                _stream.Dispose();
                if (!_published && File.Exists(_path)) File.Delete(_path);
            }
        }

        private sealed class RecordingLog : ILoggingService
        {
            public List<Exception> Errors { get; } = new List<Exception>();
            public string LogDirectory => string.Empty;
            public void Debug(string operation, string message) { }
            public void Info(string operation, string message) { }
            public void Warning(string operation, string message) { }
            public void Error(string operation, string message, Exception exception)
            {
                Errors.Add(exception);
            }
            public void HttpFailure(
                string operation,
                int statusCode,
                string awsErrorCode,
                string message,
                string objectKey,
                string uploadId) { }
        }
    }
}
