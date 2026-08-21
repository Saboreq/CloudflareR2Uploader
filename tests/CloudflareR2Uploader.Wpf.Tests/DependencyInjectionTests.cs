using System;
using System.IO;
using CloudflareR2Uploader.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Wpf.Tests
{
    [TestClass]
    public sealed class DependencyInjectionTests
    {
        [TestMethod]
        public void UploadQueueLifecycle_ResolvesTheConcreteSingletonByReference()
        {
            ServiceCollection services = new();
            RecordingLog log = new();
            services.AddSingleton<ILoggingService>(log);
            services.AddSingleton(new UploadStateStore(log, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
            App.AddUploadQueueServices(services);

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.AreSame(
                provider.GetRequiredService<UploadQueueService>(),
                provider.GetRequiredService<IUploadQueueLifecycle>());
        }

        private sealed class RecordingLog : ILoggingService
        {
            public void Debug(string operation, string message) { }
            public void Info(string operation, string message) { }
            public void Warning(string operation, string message) { }
            public void Error(string operation, string message, Exception exception) { }
            public void HttpFailure(string operation, int statusCode, string awsErrorCode, string message, string objectKey, string uploadId) { }
            public string LogDirectory => Path.GetTempPath();
        }
    }
}
