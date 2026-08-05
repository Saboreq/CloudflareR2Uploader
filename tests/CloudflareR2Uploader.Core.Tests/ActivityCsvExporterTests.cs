#nullable enable

using System;
using System.Collections.Generic;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class ActivityCsvExporterTests
    {
        [TestMethod]
        public void EscapeField_LeavesOrdinaryValuesAlone()
        {
            Assert.AreEqual("assets/builds/app.zip", ActivityCsvExporter.EscapeField("assets/builds/app.zip"));
            Assert.AreEqual(string.Empty, ActivityCsvExporter.EscapeField(null));
        }

        [TestMethod]
        public void EscapeField_QuotesCommasQuotesAndNewlines()
        {
            Assert.AreEqual("\"a,b\"", ActivityCsvExporter.EscapeField("a,b"));
            Assert.AreEqual("\"say \"\"hi\"\"\"", ActivityCsvExporter.EscapeField("say \"hi\""));
            Assert.AreEqual("\"line\nbreak\"", ActivityCsvExporter.EscapeField("line\nbreak"));
            Assert.AreEqual("\"carriage\rreturn\"", ActivityCsvExporter.EscapeField("carriage\rreturn"));
        }

        [TestMethod]
        public void EscapeField_QuotesValuesWithSignificantWhitespace()
        {
            Assert.AreEqual("\" leading\"", ActivityCsvExporter.EscapeField(" leading"));
            Assert.AreEqual("\"trailing \"", ActivityCsvExporter.EscapeField("trailing "));
        }

        [TestMethod]
        public void EscapeField_NeutralisesSpreadsheetFormulaPrefixes()
        {
            // An object key may legitimately start with any of these; a spreadsheet would
            // otherwise evaluate the cell.
            Assert.AreEqual("'=cmd|'/c calc'!A1", ActivityCsvExporter.EscapeField("=cmd|'/c calc'!A1"));
            Assert.AreEqual("'+1", ActivityCsvExporter.EscapeField("+1"));
            Assert.AreEqual("'-1", ActivityCsvExporter.EscapeField("-1"));
            Assert.AreEqual("'@import", ActivityCsvExporter.EscapeField("@import"));
        }

        [TestMethod]
        public void Export_WritesAHeaderAndOneCrlfTerminatedRowPerRecord()
        {
            List<ActivityRecord> records = new List<ActivityRecord>
            {
                new ActivityRecord
                {
                    Action = ActivityAction.Upload,
                    Result = ActivityResult.Success,
                    Target = "assets/builds/app,v2.zip",
                    BucketName = "storage",
                    SizeBytes = 1024,
                    DurationMilliseconds = 420,
                    TimestampUtc = new DateTime(2026, 8, 4, 13, 15, 4, DateTimeKind.Utc)
                }
            };

            string csv = ActivityCsvExporter.Export(records);
            string[] lines = csv.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

            Assert.AreEqual(2, lines.Length);
            StringAssert.StartsWith(lines[0], "Timestamp (UTC),Action,Result,Object,Destination,Bucket");
            StringAssert.Contains(lines[1], "2026-08-04T13:15:04");
            StringAssert.Contains(lines[1], "\"assets/builds/app,v2.zip\"");
            StringAssert.Contains(lines[1], ",1024,420,");
        }

        [TestMethod]
        public void Export_LeavesUnknownSizeAndDurationEmptyRatherThanWritingZero()
        {
            string csv = ActivityCsvExporter.Export(new List<ActivityRecord>
            {
                new ActivityRecord
                {
                    Action = ActivityAction.PublicLink,
                    Result = ActivityResult.Copied,
                    Target = "assets/a.png",
                    BucketName = "storage",
                    SizeBytes = null,
                    DurationMilliseconds = null
                }
            });

            StringAssert.Contains(csv, "assets/a.png,,storage,,,,");
        }

        [TestMethod]
        public void Export_CannotContainASignedUrlBecauseRecordsAreSanitisedFirst()
        {
            ActivityRecord sanitized = ActivitySanitizer.Sanitize(new ActivityRecord
            {
                Action = ActivityAction.TemporaryLink,
                Target = "https://acct.r2.cloudflarestorage.com/storage/a.zip?X-Amz-Signature=deadbeefdeadbeef"
            });

            string csv = ActivityCsvExporter.Export(new[] { sanitized });

            Assert.IsFalse(csv.Contains("X-Amz-Signature", StringComparison.OrdinalIgnoreCase));
        }
    }
}
