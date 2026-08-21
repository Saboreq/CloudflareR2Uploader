using System;
using System.IO;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class BulkLocalPathMapperTests
    {
        [TestMethod] public void Map_SanitizesTraversalReservedNamesAndDuplicatesWithoutEscaping()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                BulkObjectOperationPlan plan = new BulkObjectOperationPlan();
                plan.Objects.Add(new BulkObjectEntry { Key="1", RelativePath="../CON/file?.txt" });
                plan.Objects.Add(new BulkObjectEntry { Key="2", RelativePath="../CON/file*.txt" });
                new BulkLocalPathMapper().Map(plan, directory.Path);
                StringAssert.StartsWith(plan.Objects[0].LocalPath, Path.GetFullPath(directory.Path));
                StringAssert.Contains(plan.Objects[0].RelativePath, "_");
                Assert.AreNotEqual(plan.Objects[0].LocalPath.ToLowerInvariant(), plan.Objects[1].LocalPath.ToLowerInvariant());
            }
        }
        [TestMethod] public void Sanitize_PreservesUnicodeAndTrimsTrailingDotsAndSpaces()
        {
            string value = new BulkLocalPathMapper().SanitizeRelativePath("資料/hello. ");
            StringAssert.Contains(value, "資料"); Assert.IsFalse(value.EndsWith('.')); Assert.IsFalse(value.EndsWith(' '));
        }
        [TestMethod] public void ConflictNaming_PreservesExtensionAndUsesDeterministicNumbers()
        {
            string result = BulkConflictNameUtility.FindAvailable(@"C:\x\file.tar.gz", value => value.EndsWith("file.tar.gz") || value.EndsWith("file.tar (1).gz"));
            StringAssert.EndsWith(result, "file.tar (2).gz");
        }
    }
}
