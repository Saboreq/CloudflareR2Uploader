using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using CloudflareR2Uploader.Wpf.Services;
using CloudflareR2Uploader.Wpf.Converters;
using CloudflareR2Uploader.Wpf.ViewModels;
using CloudflareR2Uploader.Wpf.ViewModels.Activity;
using CloudflareR2Uploader.Wpf.ViewModels.Dialogs;
using CloudflareR2Uploader.Wpf.ViewModels.Files;
using CloudflareR2Uploader.Wpf.ViewModels.Upload;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Wpf.Tests
{
    [TestClass]
    public sealed class ViewModelBehaviorTests
    {
        [TestMethod]
        public void TextPromptValidationControlsConfirmCommand()
        {
            TextPromptViewModel viewModel = new(new TextPromptRequest
            {
                Title = "Rename",
                Label = "Name",
                InitialValue = string.Empty,
                Validate = value => string.IsNullOrWhiteSpace(value) ? "A name is required." : null
            });

            Assert.IsFalse(viewModel.ConfirmCommand.CanExecute(null));
            Assert.IsTrue(viewModel.HasError);

            viewModel.Value = "archive.zip";

            Assert.IsTrue(viewModel.ConfirmCommand.CanExecute(null));
            Assert.IsFalse(viewModel.HasError);
        }

        [TestMethod]
        public void UploadQueueRowMapsStatusAndCommandActions()
        {
            UploadQueueItem item = new("C:\\uploads\\video.mp4", "video.mp4", 1_000, DateTime.UtcNow);
            item.SetStatus(UploadItemStatus.Uploading);
            item.SetMultipartLayout(4);
            item.ApplyProgress(new UploadProgressInfo(500, 1_000, 100, 2, 4));

            int pauseCalls = 0;
            UploadQueueItemViewModel viewModel = new(
                item,
                _ => pauseCalls++,
                _ => { },
                _ => { },
                _ => { },
                _ => { });

            Assert.AreEqual(UploadItemStatus.Uploading, viewModel.Status);
            Assert.AreEqual(50d, viewModel.Percent);
            Assert.IsTrue(viewModel.CanPause);
            Assert.IsFalse(viewModel.CanRetry);
            StringAssert.Contains(viewModel.DetailText, "2");
            viewModel.PauseOrResumeCommand.Execute(null);
            Assert.AreEqual(1, pauseCalls);

            item.SetFailure("The service rejected this request", "secret technical detail");
            viewModel.Refresh();

            Assert.IsTrue(viewModel.CanRetry);
            Assert.IsTrue(viewModel.CanRemove);
            Assert.IsFalse(viewModel.CanCopyLink);
        }

        [TestMethod]
        public void FileRowUsesAccessibleTypeAndFolderSemantics()
        {
            FileRowViewModel image = new(new R2BrowserItem
            {
                Key = "images/cover.webp",
                DisplayName = "cover.webp",
                Size = 2_048,
                LastModifiedUtc = DateTime.UtcNow
            });
            FileRowViewModel folder = new(new R2BrowserItem
            {
                Prefix = "images/",
                DisplayName = "images",
                IsFolder = true
            });

            Assert.AreEqual("Icon.FileImage", image.IconKey);
            StringAssert.Contains(image.AutomationName, "cover.webp");
            Assert.AreEqual("Icon.Folder", folder.IconKey);
            StringAssert.StartsWith(folder.AutomationName, "Folder images");
        }

        [TestMethod]
        public void MultiSelectionInspectorBuildsRealSelectionSummaryWithoutPreviewingAnItem()
        {
            string cacheRoot = Path.Combine(Path.GetTempPath(), "r2uploader-wpf-tests", Guid.NewGuid().ToString("N"));
            using PreviewCacheService cache = new(cacheRoot);
            FakeSession session = new();
            FakeLauncher launcher = new();
            FakeToastService toasts = new();
            FakeLog log = new();
            using PreviewViewModel preview = new(new R2PreviewService(cache), launcher, toasts, session, log);
            using FileInspectorViewModel inspector = new(
                session,
                new FakeClipboard(),
                launcher,
                toasts,
                log,
                new R2ObjectDetailsService(),
                preview);

            inspector.Show(new[]
            {
                new R2BrowserItem { Key = "a.png", DisplayName = "a.png", Size = 1_024 },
                new R2BrowserItem { Key = "b.txt", DisplayName = "b.txt", Size = 2_048 },
                new R2BrowserItem { Prefix = "folder/", DisplayName = "folder", IsFolder = true }
            }, "releases/");

            Assert.IsTrue(inspector.HasSelection);
            Assert.IsTrue(inspector.IsMultiSelection);
            Assert.AreEqual("3 objects selected", inspector.Title);
            Assert.AreEqual("3.00 KB", inspector.TotalSizeText);
            Assert.AreEqual(3, inspector.SelectedObjects.Count);
            Assert.AreEqual(PreviewState.Empty, preview.State);
            StringAssert.Contains(preview.PlaceholderTitle, "Multiple");
        }

        [TestMethod]
        public void SettingsValidationRejectsUnsafeOrOutOfRangeValues()
        {
            StringAssert.Contains(SettingsViewModel.ValidateEndpoint("http://example.com", string.Empty)!, "HTTPS");
            StringAssert.Contains(SettingsViewModel.ValidateEndpoint("https://user:pass@example.com", string.Empty)!, "credentials");
            Assert.IsNull(SettingsViewModel.ValidatePublicBaseUrl("cdn.example.com"));
            Assert.IsNotNull(SettingsViewModel.ValidateRange("0", 1, 8, "parallel transfers"));
            Assert.IsNull(SettingsViewModel.ValidatePartSize("64"));
        }

        [TestMethod]
        public void SettingsDraftTracksUnsavedChangesAndRestoresCleanState()
        {
            FakeSession session = new();
            using UpdateService updates = new();
            using SettingsViewModel viewModel = new(
                session,
                new FakeDialogService(),
                new FakeClipboard(),
                new FakeStartup(),
                new R2ConnectionTester(new FakeLog()),
                updates,
                new FakeLog(),
                new FakeLauncher(),
                new FakeExitCoordinator(),
                new FakeApplicationController());

            Assert.IsFalse(viewModel.IsDirty);
            Assert.IsFalse(viewModel.CanSave);

            bool original = viewModel.ConfirmDeletes;
            viewModel.ConfirmDeletes = !original;

            Assert.IsTrue(viewModel.IsDirty);
            Assert.IsTrue(viewModel.CanSave);

            viewModel.ConfirmDeletes = original;

            Assert.IsFalse(viewModel.IsDirty);
            Assert.IsFalse(viewModel.CanSave);
        }

        [TestMethod]
        public void ChoiceRecordsRenderOnlyTheirDisplayNames()
        {
            Assert.AreEqual("Ask each time", new OverwriteChoice("Ask each time", OverwriteBehavior.Ask).ToString());
            Assert.AreEqual("Newest", new SortOption("Newest", BrowserSortColumn.LastModified, BrowserSortDirection.Descending).ToString());
            Assert.AreEqual("30 days", new DateRangeChoice("30 days", 30).ToString());
            Assert.AreEqual("5 attempts", new RetryChoice("5 attempts", 5).ToString());
        }

        [TestMethod]
        public void EnumMatchConverterRoundTripsInspectorTabs()
        {
            EnumMatchConverter converter = new();

            Assert.AreEqual(true, converter.Convert(InspectorTab.Metadata, typeof(bool), "Metadata", CultureInfo.InvariantCulture));
            Assert.AreEqual(
                InspectorTab.Metadata,
                converter.ConvertBack(true, typeof(InspectorTab), "Metadata", CultureInfo.InvariantCulture));
            Assert.AreSame(
                System.Windows.Data.Binding.DoNothing,
                converter.ConvertBack(false, typeof(InspectorTab), "Metadata", CultureInfo.InvariantCulture));
        }

        private sealed class FakeSession : IAppSession
        {
            private AppSettings _settings;
            private readonly Dictionary<string, R2Credentials> _credentials = new(StringComparer.OrdinalIgnoreCase);

            public FakeSession()
            {
                _settings = new AppSettings();
                _settings.NormalizeBucketProfiles();
                R2BucketProfile profile = _settings.GetActiveBucketProfile();
                profile.AccountId = "test-account";
                profile.BucketName = "test-bucket";
                _settings.AccountId = profile.AccountId;
                _settings.BucketName = profile.BucketName;
                _credentials[profile.Id] = new R2Credentials("access-key", "secret-key");
            }

            public AppSettings Settings => _settings.Clone();
            public R2Credentials Credentials => GetCredentials(_settings.ActiveBucketProfileId);
            public ProfileToken Token { get; private set; } = new("test", 1);
            public R2BucketProfile ActiveProfile => _settings.GetActiveBucketProfile().Clone();
            public IReadOnlyList<R2BucketProfile> Profiles => _settings.BucketProfiles.Select(profile => profile.Clone()).ToList();
            public bool IsConfigured => false;
            public event EventHandler? Changed;
            public event EventHandler? ProfileChanged;

            public void SelectProfile(string profileId)
            {
                _settings.ActiveBucketProfileId = profileId;
                Token = new ProfileToken(profileId, Token.Generation + 1);
                ProfileChanged?.Invoke(this, EventArgs.Empty);
                Changed?.Invoke(this, EventArgs.Empty);
            }

            public bool ApplyAndSave(AppSettings settings)
            {
                _settings = settings.Clone();
                Changed?.Invoke(this, EventArgs.Empty);
                return true;
            }

            public bool SetCredentials(string profileId, R2Credentials credentials)
            {
                _credentials[profileId] = credentials;
                return true;
            }

            public R2Credentials GetCredentials(string profileId) =>
                _credentials.TryGetValue(profileId, out R2Credentials? value) ? value : R2Credentials.Empty;

            public bool ForgetAllCredentials()
            {
                _credentials.Clear();
                return true;
            }
        }

        private sealed class FakeClipboard : IClipboardService
        {
            public void SetText(string text) { }
        }

        private sealed class FakeLauncher : IProcessLauncher
        {
            public void Open(string target) { }
            public void Start(string executable, string arguments) { }
        }

        private sealed class FakeStartup : IStartupRegistrationService
        {
            public bool IsEnabled(string executablePath) => false;
            public bool SetEnabled(bool enabled, string executablePath, out string error) { error = string.Empty; return true; }
            public string RegisteredCommand => string.Empty;
        }

        private sealed class FakeExitCoordinator : IApplicationExitCoordinator
        {
            public event EventHandler? ExitCommitted;
            public Task<bool> PrepareAsync() => Task.FromResult(true);
            public void Commit() => ExitCommitted?.Invoke(this, EventArgs.Empty);
        }

        private sealed class FakeApplicationController : IApplicationController
        {
            public void Shutdown() { }
        }

        private sealed class FakeToastService : IToastService
        {
            private readonly ObservableCollection<ToastViewModel> _toasts = new();
            public ReadOnlyObservableCollection<ToastViewModel> Toasts { get; }

            public FakeToastService() => Toasts = new ReadOnlyObservableCollection<ToastViewModel>(_toasts);
            public void ShowSuccess(string message) { }
            public void ShowError(string message, string? actionLabel = null, Action? action = null) { }
            public ToastViewModel ShowProgress(string message) => new(ToastKind.Progress, message, _ => { });
            public void Dismiss(ToastViewModel toast) { }
        }

        private sealed class FakeLog : ILoggingService
        {
            public void Debug(string operation, string message) { }
            public void Info(string operation, string message) { }
            public void Warning(string operation, string message) { }
            public void Error(string operation, string message, Exception exception) { }
            public void HttpFailure(string operation, int statusCode, string awsErrorCode, string message, string objectKey, string uploadId) { }
            public string LogDirectory => Path.GetTempPath();
        }

        private sealed class FakeDialogService : IDialogService
        {
            public Task ShowMessageAsync(string title, string message, string? details = null) => Task.CompletedTask;
            public Task ShowErrorAsync(string title, string message, string? technicalDetails = null) => Task.CompletedTask;
            public Task<bool> ConfirmAsync(string title, string message, string confirmLabel, bool isDestructive = false) => Task.FromResult(false);
            public Task<ConfirmationResult> ConfirmAsync(string title, string message, string confirmLabel, string optionLabel, bool isDestructive = false) => Task.FromResult(default(ConfirmationResult));
            public Task<string?> PromptForTextAsync(TextPromptRequest request) => Task.FromResult<string?>(null);
            public Task<OverwriteDecision> AskOverwriteAsync(string objectKey, long existingLength, DateTime existingModifiedUtc, long localLength) => Task.FromResult(OverwriteDecision.Skip);
            public Task<bool?> AskKeepPartsOnCancelAsync(int activeMultipartCount) => Task.FromResult<bool?>(null);
            public Task ShowTemporaryLinkAsync(TemporaryLinkDialogViewModel viewModel) => Task.CompletedTask;
            public Task<bool> ShowSettingsAsync(string? initialSection = null) => Task.FromResult(false);
            public string? BrowseForFolder(string title, string? initialDirectory = null) => null;
            public IReadOnlyList<string> BrowseForFiles(string title) => Array.Empty<string>();
            public string? BrowseForSaveFile(string title, string suggestedFileName, string filter) => null;
        }
    }
}
