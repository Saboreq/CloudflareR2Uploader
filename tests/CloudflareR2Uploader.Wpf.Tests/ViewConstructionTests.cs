using System.Windows;
using CloudflareR2Uploader.Wpf.Views.Activity;
using CloudflareR2Uploader.Wpf.Views.Dialogs;
using CloudflareR2Uploader.Wpf.Views.Files;
using CloudflareR2Uploader.Wpf.Views.Upload;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Wpf.Tests
{
    [TestClass]
    public sealed class ViewConstructionTests
    {
        [STATestMethod]
        public void ProductionViewsLoadAllReferencedResources()
        {
            if (Application.Current is null)
            {
                App application = new();
                application.InitializeComponent();
            }

            _ = new UploadView();
            _ = new FilesView();
            _ = new FileInspectorView();
            _ = new InspectorPreviewView();
            _ = new ActivityView();
            _ = new MessageDialog();
            _ = new TextPromptDialog();
            _ = new OverwritePromptDialog();
            _ = new TemporaryLinkDialog();
            SettingsWindow settings = new();
            Assert.AreEqual(880d, settings.Width);
            Assert.AreEqual(610d, settings.Height);
            Assert.AreEqual(WindowStyle.None, settings.WindowStyle);
            Assert.IsTrue(settings.AllowsTransparency);
        }
    }
}
