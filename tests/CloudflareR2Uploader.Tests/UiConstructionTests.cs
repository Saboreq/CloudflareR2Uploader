using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using CloudflareR2Uploader.Controls;
using CloudflareR2Uploader.Forms;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using CloudflareR2Uploader.Theming;
using CloudflareR2Uploader.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class UiConstructionTests
    {
        private TemporaryDirectory _appData;

        [TestInitialize]
        public void RedirectApplicationData()
        {
            _appData = new TemporaryDirectory();
            AppPaths.OverrideRootForTesting(_appData.Path);
        }

        [TestCleanup]
        public void RestoreApplicationData()
        {
            AppPaths.OverrideRootForTesting(null);
            if (_appData != null) _appData.Dispose();
        }

        [TestMethod]
        public void MainForm_DesignerControlsConstructOnStaThread()
        {
            Exception failure = null;
            bool constructed = false;
            bool branded = false;
            bool iconLoaded = false;
            bool bucketSelectorPresent = false;
            bool browserControlsPresent = false;

            Thread thread = new Thread(() =>
            {
                try
                {
                    using (MainForm form = new MainForm(null))
                    {
                        constructed = form.Controls.Count > 0;
                        branded = FindBrandMark(form) != null;
                        iconLoaded = form.Icon != null;
                        bucketSelectorPresent = FindByName(form, "bucketSelectorComboBox") is ModernComboBox;
                        ModernButton uploadPage = FindByName(form, "uploadPageButton") as ModernButton;
                        ModernButton bucketFilesPage = FindByName(form, "bucketFilesPageButton") as ModernButton;
                        DataGridView browserGrid = FindByName(form, "browserGrid") as DataGridView;
                        ContextMenuStrip browserMenu = browserGrid == null ? null : browserGrid.ContextMenuStrip;
                        browserControlsPresent =
                            uploadPage != null && uploadPage.ButtonStyle == ModernButtonStyle.Primary &&
                            bucketFilesPage != null && bucketFilesPage.Text == "Files" &&
                            bucketFilesPage.ButtonStyle == ModernButtonStyle.Ghost &&
                            browserGrid != null && browserGrid.ReadOnly && browserGrid.MultiSelect &&
                            !browserGrid.AllowUserToAddRows && !browserGrid.AllowUserToDeleteRows &&
                            browserGrid.SelectionMode == DataGridViewSelectionMode.FullRowSelect &&
                            browserGrid.Columns.Count == 4 &&
                            FindByName(form, "browserRefreshButton") is ModernButton &&
                            FindByName(form, "browserNewFolderButton") is ModernButton &&
                            FindByName(form, "browserUpButton") is ModernButton &&
                            FindByName(form, "browserPreviousButton") is ModernButton &&
                            FindByName(form, "browserNextButton") is ModernButton &&
                            FindByName(form, "browserFilterTextBox") is ModernTextBox &&
                            FindByName(form, "browserDetailsToggleButton") is Button &&
                            FindByName(form, "pageTransitionOverlay") is PageTransitionOverlay &&
                            browserMenu != null &&
                            FindMenuItem(browserMenu, "browserDownloadMenuItem") != null &&
                            FindMenuItem(browserMenu, "browserOverwriteMenuItem") != null &&
                            FindMenuItem(browserMenu, "browserCopyMenuItem") != null &&
                            FindMenuItem(browserMenu, "browserPasteMenuItem") != null &&
                            FindMenuItem(browserMenu, "browserNewFolderMenuItem") != null &&
                            FindMenuItem(browserMenu, "browserRenameMenuItem") != null &&
                            FindMenuItem(browserMenu, "browserMoveMenuItem") != null &&
                            FindMenuItem(browserMenu, "browserDeleteMenuItem") != null;
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(15)), "MainForm construction timed out.");
            if (failure != null)
                Assert.Fail("MainForm construction failed: " + failure);
            Assert.IsTrue(constructed);
            Assert.IsTrue(branded, "The shared Saboreq brand mark is missing from the main window.");
            Assert.IsTrue(iconLoaded, "The embedded application icon did not load.");
            Assert.IsTrue(bucketSelectorPresent, "The main target-bucket selector is missing.");
            Assert.IsTrue(browserControlsPresent, "The Bucket files navigation or browser controls are missing.");
        }

        [TestMethod]
        public void Theme_UsesSharedSaboreqBrandTokens()
        {
            Assert.AreEqual(Color.FromArgb(0x05, 0x05, 0x07).ToArgb(), Theme.WindowBackground.ToArgb());
            Assert.AreEqual(Color.FromArgb(0x8B, 0x5C, 0xF6).ToArgb(), Theme.Accent.ToArgb());
            Assert.AreEqual(Color.FromArgb(0x22, 0xD3, 0xEE).ToArgb(), Theme.Cyan.ToArgb());
            Assert.AreEqual(Color.FromArgb(0xF7, 0xF7, 0xF8).ToArgb(), Theme.TextPrimary.ToArgb());
        }

        [TestMethod]
        public void MainForm_AddsLargeQueueRowOnStaThread()
        {
            Exception failure = null;
            bool rowAdded = false;
            int rowBackgroundAlpha = -1;

            Thread thread = new Thread(() =>
            {
                try
                {
                    using (MainForm form = new MainForm(null))
                    {
                        UploadQueueItem item = new UploadQueueItem(
                            @"C:\data\large-file.bin",
                            "large-file.bin",
                            886L * 1024L * 1024L,
                            DateTime.UtcNow)
                        {
                            ObjectKey = "large-file.bin"
                        };

                        MethodInfo addRow = typeof(MainForm).GetMethod(
                            "AddRow",
                            BindingFlags.Instance | BindingFlags.NonPublic);
                        Assert.IsNotNull(addRow);
                        addRow.Invoke(form, new object[] { item });

                        UploadQueueItemControl row = FindUploadRow(form);
                        rowAdded = row != null;
                        if (row != null) rowBackgroundAlpha = row.BackColor.A;
                    }
                }
                catch (TargetInvocationException ex)
                {
                    failure = ex.InnerException ?? ex;
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(15)), "Adding a queue row timed out.");
            if (failure != null)
                Assert.Fail("Adding a large queue row failed: " + failure);
            Assert.IsTrue(rowAdded, "The large file was not represented by a queue row.");
            Assert.AreEqual(255, rowBackgroundAlpha, "Queue row backgrounds must be opaque.");
            Assert.AreEqual(255, Theme.SurfaceBackground.A, "The surface palette color must be opaque.");
        }

        [TestMethod]
        public void MainForm_BrowserLayoutScalesAtCommonDpiFactors()
        {
            Exception failure = null;
            bool layoutValid = true;

            Thread thread = new Thread(() =>
            {
                try
                {
                    float[] factors = { 1.0f, 1.25f, 1.5f };
                    foreach (float factor in factors)
                    {
                        using (MainForm form = new MainForm(null))
                        {
                            form.CreateControl();
                            if (factor != 1.0f) form.Scale(new SizeF(factor, factor));
                            form.PerformLayout();

                            Control browserPanel = FindByName(form, "bucketBrowserPagePanel");
                            Control grid = FindByName(form, "browserGrid");
                            Control refresh = FindByName(form, "browserRefreshButton");
                            Control up = FindByName(form, "browserUpButton");

                            layoutValid = layoutValid &&
                                form.AutoScaleMode == AutoScaleMode.Dpi &&
                                form.MinimumSize.Width >= 940 &&
                                form.MinimumSize.Height >= 760 &&
                                browserPanel != null && browserPanel.Width > 700 && browserPanel.Height > 400 &&
                                grid != null && grid.Width > 600 && grid.Height > 250 &&
                                refresh != null && refresh.Bounds.Width > 0 &&
                                up != null && up.Bounds.Width > 0;
                        }
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(15)), "DPI layout construction timed out.");
            if (failure != null) Assert.Fail("DPI layout construction failed: " + failure);
            Assert.IsTrue(layoutValid, "The browser layout collapsed at a common Windows DPI scale.");
        }

        [TestMethod]
        public void MainForm_CloseToTrayKeepsFormAliveAndExplicitExitTearsDown()
        {
            Exception failure = null;
            bool hideRequested = false;
            bool aliveAfterClose = false;
            bool disposedAfterExit = false;
            Thread thread = new Thread(() =>
            {
                try
                {
                    MainForm form = new MainForm(null);
                    EventHandler handler = (s, e) => hideRequested = true;
                    form.HideToTrayRequested += handler;
                    form.Show();
                    form.Close();
                    aliveAfterClose = !form.IsDisposed;
                    MethodInfo exit = typeof(MainForm).GetMethod("RequestRealExit", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.IsTrue((bool)exit.Invoke(form, new object[] { false }));
                    form.Close();
                    disposedAfterExit = form.IsDisposed;
                }
                catch (Exception ex) { failure = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex; }
            });
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(15)));
            if (failure != null) Assert.Fail(failure.ToString());
            Assert.IsTrue(hideRequested); Assert.IsTrue(aliveAfterClose); Assert.IsTrue(disposedAfterExit);
        }

        [TestMethod]
        public void SettingsAndPromptDialogs_DesignerControlsConstructOnStaThread()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                Exception failure = null;
                bool constructed = false;
                bool bucketManagerPresent = false;
                bool canAddBucket = false;

                Thread thread = new Thread(() =>
                {
                    try
                    {
                        SettingsService settingsService =
                            new SettingsService(null, directory.File("settings.json"));
                        CredentialProtectionService credentialService =
                            new CredentialProtectionService(null, directory.File("credentials.dat"));

                        using (SettingsForm settings = new SettingsForm(
                            null, settingsService, credentialService,
                            new AppSettings(), R2Credentials.Empty))
                        using (ErrorDialog error = new ErrorDialog("Headline", "Message", "Details"))
                        using (CancelChoiceDialog cancel = new CancelChoiceDialog(true))
                        using (R2DestinationDialog destination = new R2DestinationDialog(false, "photos/moved.bin"))
                        using (R2NameDialog name = new R2NameDialog(
                            "Rename object", "Rename file.bin", "Enter a new name.", "file.bin", "Rename"))
                        {
                            UploadQueueItem item = new UploadQueueItem(
                                @"C:\data\file.bin", "file.bin", 100, DateTime.UtcNow)
                            {
                                ObjectKey = "file.bin"
                            };
                            ExistingObjectInfo existing =
                                new ExistingObjectInfo("file.bin", 100, DateTime.UtcNow);

                            using (OverwritePromptDialog overwrite =
                                new OverwritePromptDialog(item, existing))
                            {
                                settings.CreateControl();
                                error.CreateControl();
                                cancel.CreateControl();
                                destination.CreateControl();
                                name.CreateControl();
                                overwrite.CreateControl();

                                constructed =
                                    settings.Controls.Count > 0 &&
                                    error.Controls.Count > 0 &&
                                    cancel.Controls.Count > 0 &&
                                    destination.Controls.Count > 0 &&
                                    name.Controls.Count > 0 &&
                                    overwrite.Controls.Count > 0;

                                ModernComboBox profileSelector =
                                    FindByName(settings, "profileComboBox") as ModernComboBox;
                                ModernButton addBucket =
                                    FindByName(settings, "addProfileButton") as ModernButton;
                                bucketManagerPresent =
                                    profileSelector != null &&
                                    addBucket != null &&
                                    FindByName(settings, "removeProfileButton") is ModernButton;

                                if (bucketManagerPresent)
                                {
                                    int before = profileSelector.Items.Count;
                                    addBucket.PerformClick();
                                    canAddBucket = profileSelector.Items.Count == before + 1;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                    }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();

                Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(15)), "Dialog construction timed out.");
                if (failure != null)
                    Assert.Fail("Dialog construction failed: " + failure);
                Assert.IsTrue(constructed);
                Assert.IsTrue(bucketManagerPresent, "The Settings bucket-profile manager is missing.");
                Assert.IsTrue(canAddBucket, "Adding a second bucket profile did not update the selector.");
            }
        }

        [TestMethod]
        public void UpdatePromptAndProgressDialogs_ConstructOnStaThread()
        {
            Exception failure = null;
            bool constructed = false;
            Thread thread = new Thread(() =>
            {
                try
                {
                    UpdateManifest manifest = new UpdateManifest
                    {
                        SchemaVersion = 1,
                        Version = "1.1.0",
                        InstallerUrl = "https://updates.example.test/releases/1.1.0/Setup.exe",
                        Sha256 = new string('a', 64),
                        SizeBytes = 4096,
                        Notes = "Synthetic release notes"
                    };
                    using (CancellationTokenSource cancellation = new CancellationTokenSource())
                    using (UpdateAvailableDialog prompt = new UpdateAvailableDialog("1.0.0", manifest))
                    using (UpdateDownloadDialog progress = new UpdateDownloadDialog(cancellation))
                    {
                        prompt.CreateControl();
                        progress.CreateControl();
                        progress.Report(new UpdateDownloadProgress { BytesReceived = 2048, TotalBytes = 4096, Percentage = 50 });
                        constructed = prompt.Controls.Count > 0 && progress.Controls.Count > 0;
                    }
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(15)), "Update dialog construction timed out.");
            if (failure != null) Assert.Fail("Update dialog construction failed: " + failure);
            Assert.IsTrue(constructed);
        }

        private static UploadQueueItemControl FindUploadRow(Control root)
        {
            foreach (Control child in root.Controls)
            {
                UploadQueueItemControl row = child as UploadQueueItemControl;
                if (row != null) return row;

                row = FindUploadRow(child);
                if (row != null) return row;
            }

            return null;
        }

        private static BrandMarkControl FindBrandMark(Control root)
        {
            foreach (Control child in root.Controls)
            {
                BrandMarkControl mark = child as BrandMarkControl;
                if (mark != null) return mark;

                mark = FindBrandMark(child);
                if (mark != null) return mark;
            }

            return null;
        }

        private static Control FindByName(Control root, string name)
        {
            if (root == null) return null;
            foreach (Control child in root.Controls)
            {
                if (string.Equals(child.Name, name, StringComparison.Ordinal)) return child;
                Control nested = FindByName(child, name);
                if (nested != null) return nested;
            }
            return null;
        }

        private static ToolStripItem FindMenuItem(ContextMenuStrip menu, string name)
        {
            if (menu == null) return null;
            foreach (ToolStripItem item in menu.Items)
                if (string.Equals(item.Name, name, StringComparison.Ordinal)) return item;
            return null;
        }
    }
}
