using System;
using System.IO;
using System.Net;
using System.Threading;
using Amazon.S3;
using CloudflareR2Uploader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class TransientErrorClassifierTests
    {
        [TestMethod]
        public void Classify_RecognizesTransientHttpStatuses()
        {
            foreach (HttpStatusCode status in new[]
            {
                HttpStatusCode.RequestTimeout,
                (HttpStatusCode)429,
                HttpStatusCode.InternalServerError,
                HttpStatusCode.BadGateway,
                HttpStatusCode.ServiceUnavailable,
                HttpStatusCode.GatewayTimeout
            })
            {
                AmazonS3Exception error = new AmazonS3Exception("temporary") { StatusCode = status };
                Assert.AreEqual(ErrorClassification.Transient,
                    TransientErrorClassifier.Classify(error, CancellationToken.None),
                    status.ToString());
            }
        }

        [TestMethod]
        public void Classify_DoesNotRetryPermanentOrUserCancelledFailures()
        {
            AmazonS3Exception denied = new AmazonS3Exception("denied")
            {
                StatusCode = HttpStatusCode.Forbidden,
                ErrorCode = "AccessDenied"
            };
            Assert.AreEqual(ErrorClassification.Permanent,
                TransientErrorClassifier.Classify(denied, CancellationToken.None));
            Assert.AreEqual(ErrorClassification.Permanent,
                TransientErrorClassifier.Classify(new FileNotFoundException(), CancellationToken.None));

            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                source.Cancel();
                Assert.AreEqual(ErrorClassification.UserCancelled,
                    TransientErrorClassifier.Classify(new OperationCanceledException(), source.Token));
            }
        }
    }
}
