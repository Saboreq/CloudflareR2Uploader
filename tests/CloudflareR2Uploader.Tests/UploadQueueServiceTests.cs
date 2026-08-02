using System.IO;
using System.Linq;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class UploadQueueServiceTests
    {
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
    }
}
