using System.Text;
using System.Linq;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class PreviewServiceTests
    {
        [TestMethod] public void Classify_UsesMimeAndExtensionAndKeepsActiveFormatsAsSource()
        {
            Assert.AreEqual(R2PreviewKind.Json, R2PreviewService.Classify("application/json", "x.bin"));
            Assert.AreEqual(R2PreviewKind.Pdf, R2PreviewService.Classify(null, "x.PDF"));
            Assert.AreEqual(R2PreviewKind.HtmlSource, R2PreviewService.Classify("text/html", "x.html"));
            Assert.AreEqual(R2PreviewKind.SvgSource, R2PreviewService.Classify("image/svg+xml", "x.svg"));
        }
        [TestMethod] public void TextFormatting_HandlesBomJsonInvalidJsonAndBinary()
        {
            byte[] json = Encoding.UTF8.GetBytes("{\"a\":1}");
            R2PreviewResult formatted = R2PreviewService.FormatText(R2PreviewKind.Json, json, true);
            StringAssert.Contains(formatted.Text, "\"a\": 1"); Assert.IsTrue(formatted.Truncated);
            Assert.IsFalse(string.IsNullOrEmpty(R2PreviewService.FormatText(R2PreviewKind.Json, Encoding.UTF8.GetBytes("{"), false).Warning));
            Assert.AreEqual(R2PreviewKind.Unsupported, R2PreviewService.FormatText(R2PreviewKind.Text, new byte[] { 1, 0, 2 }, false).Kind);
            Assert.AreEqual("hello", R2PreviewService.Decode(new byte[] { 0xEF,0xBB,0xBF, (byte)'h',(byte)'e',(byte)'l',(byte)'l',(byte)'o' }));
            byte[] utf16 = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("hello")).ToArray();
            Assert.IsFalse(R2PreviewService.IsProbablyBinary(utf16)); Assert.AreEqual("hello", R2PreviewService.Decode(utf16));
        }
        [TestMethod] public void XmlFormatting_ProhibitsDtd()
        {
            R2PreviewResult result = R2PreviewService.FormatText(R2PreviewKind.Xml, Encoding.UTF8.GetBytes("<!DOCTYPE x [<!ENTITY y SYSTEM 'file:///c:/x'>]><x>&y;</x>"), false);
            Assert.IsFalse(string.IsNullOrEmpty(result.Warning));
        }
    }
}
