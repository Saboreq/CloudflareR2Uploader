using System;
using System.Collections.Generic;
using CloudflareR2Uploader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class StartupAndSingleInstanceTests
    {
        [TestMethod] public void StartupRegistration_QuotesBackgroundRepairsAndDisablesFakeStore()
        {
            FakeStore store = new FakeStore(); StartupRegistrationService service = new StartupRegistrationService(null, store); string error;
            Assert.IsTrue(service.SetEnabled(true, @"C:\Program Files\R2\CloudflareR2Uploader.exe", out error));
            Assert.AreEqual("\"C:\\Program Files\\R2\\CloudflareR2Uploader.exe\" --background", store.Value);
            Assert.IsTrue(service.IsEnabled(@"C:\Program Files\R2\CloudflareR2Uploader.exe"));
            Assert.IsTrue(service.SetEnabled(false, null, out error)); Assert.IsNull(store.Value);
        }
        [TestMethod] public void SingleInstanceHelpers_AreStableSafeAndIgnoreUnknownArguments()
        {
            string first = SingleInstanceCoordinator.HashUserIdentifier("S-1-5-21-test"); Assert.AreEqual(first, SingleInstanceCoordinator.HashUserIdentifier("S-1-5-21-test")); Assert.AreEqual(24, first.Length);
            Assert.AreEqual(InstanceLaunchCommand.Background, SingleInstanceCoordinator.ParseArguments(new[] { "--unknown", "--background" }));
            Assert.AreEqual(InstanceLaunchCommand.Show, SingleInstanceCoordinator.ParseArguments(new[] { "--unknown" }));
        }
        private sealed class FakeStore : IStartupRegistrationStore
        {
            public string Value; public string Read(string name) { return Value; } public void Write(string name, string value) { Value = value; } public void Delete(string name) { Value = null; }
        }
    }
}
