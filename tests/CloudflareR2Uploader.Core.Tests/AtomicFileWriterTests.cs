using System;
using System.IO;
using CloudflareR2Uploader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class AtomicFileWriterTests
    {
        // Break caught: replacing a destination by deleting it before a failed move loses the last valid bytes.
        [TestMethod]
        public void Write_WhenCommitFails_PreservesDestinationAndCleansTemporaryFile()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string destination = directory.File("settings.json");
                byte[] oldBytes = { 0x1A, 0x2B, 0x3C, 0x4D };
                byte[] newBytes = { 0x90, 0x81, 0x72 };
                File.WriteAllBytes(destination, oldBytes);
                AtomicFileWriter writer = new AtomicFileWriter(new ThrowingCommitter());

                Assert.ThrowsException<IOException>(() =>
                    writer.Write(destination, stream =>
                    {
                        stream.Write(newBytes, 0, newBytes.Length);
                    }));

                CollectionAssert.AreEqual(oldBytes, File.ReadAllBytes(destination));
                Assert.AreEqual(0, Directory.GetFiles(directory.Path, "*.tmp").Length);
            }
        }

        // Break caught: a cleanup failure must not hide the commit failure that explains why the write did not persist.
        [TestMethod]
        public void Write_WhenCommitAndCleanupFail_RethrowsCommitFailure()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string destination = directory.File("settings.json");
                AtomicFileWriter writer = new AtomicFileWriter(new ThrowingCommitter(), new ThrowingCleanup());

                IOException exception = Assert.ThrowsException<IOException>(() =>
                    writer.Write(destination, stream => stream.WriteByte(0x6E)));

                Assert.AreEqual("synthetic replacement failure", exception.Message);
            }
        }

        // Break caught: when a commit succeeds, a cleanup failure must be returned instead of silently leaving residue.
        [TestMethod]
        public void Write_WhenCleanupFailsAfterSuccessfulCommit_ThrowsCleanupFailureAndPersistsBytes()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string destination = directory.File("settings.json");
                byte[] expectedBytes = { 0x51, 0x62, 0x73, 0x84 };
                AtomicFileWriter writer = new AtomicFileWriter(new MovingCommitter(), new ThrowingCleanup());

                IOException exception = Assert.ThrowsException<IOException>(() =>
                    writer.Write(destination, stream => stream.Write(expectedBytes, 0, expectedBytes.Length)));

                Assert.AreEqual("synthetic cleanup failure", exception.Message);
                CollectionAssert.AreEqual(expectedBytes, File.ReadAllBytes(destination));
            }
        }

        private sealed class ThrowingCommitter : IAtomicFileCommitter
        {
            public void Commit(string temporaryPath, string destinationPath) =>
                throw new IOException("synthetic replacement failure");
        }

        private sealed class MovingCommitter : IAtomicFileCommitter
        {
            public void Commit(string temporaryPath, string destinationPath) => File.Move(temporaryPath, destinationPath);
        }

        private sealed class ThrowingCleanup : IAtomicFileCleanup
        {
            public void Delete(string temporaryPath) => throw new IOException("synthetic cleanup failure");
        }
    }
}
