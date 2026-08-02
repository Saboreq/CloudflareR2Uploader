using CloudflareR2Uploader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class MimeTypeServiceTests
    {
        [TestMethod]
        public void GetContentType_RecognizesCommonTypesCaseInsensitively()
        {
            Assert.AreEqual("image/png", MimeTypeService.GetContentType("image.PNG"));
            Assert.AreEqual("video/mp4", MimeTypeService.GetContentType("movie.mp4"));
            Assert.AreEqual("application/pdf", MimeTypeService.GetContentType("manual.pdf"));
            Assert.AreEqual(
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                MimeTypeService.GetContentType("report.docx"));
        }

        [TestMethod]
        public void GetContentType_FallsBackSafely()
        {
            Assert.AreEqual(MimeTypeService.DefaultContentType, MimeTypeService.GetContentType("README"));
            Assert.AreEqual(MimeTypeService.DefaultContentType, MimeTypeService.GetContentType("file.unknown"));
            Assert.IsTrue(MimeTypeService.MappedExtensionCount >= 50);
        }
    }
}
