using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using CloudflareR2Uploader.Wpf.Services;
using CloudflareR2Uploader.Wpf.ViewModels;
using CloudflareR2Uploader.Wpf.ViewModels.Dialogs;
using CloudflareR2Uploader.Wpf.ViewModels.Files;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Wpf.Tests
{
    /// <summary>
    /// The inspector and its preview deliberately raise intent rather than acting: they hold
    /// no selection, no R2 services and no dialogs. That only works if something subscribes.
    /// <para>
    /// These tests exist because that wiring was once missing, which left the inspector's
    /// close, download, copy-key, temporary-link and retry buttons silently doing nothing
    /// while still looking enabled. An unsubscribed event raises no error, so only a test
    /// like this catches the regression.
    /// </para>
    /// </summary>
    [TestClass]
    public sealed class InspectorWiringTests
    {
        [TestMethod]
        public void InspectorCloseButtonHidesThePanel()
        {
            using Harness harness = new();

            harness.Files.IsInspectorVisible = true;
            harness.Inspector.CloseCommand.Execute(null);

            Assert.IsFalse(
                harness.Files.IsInspectorVisible,
                "The inspector's close button must reach the Files screen that owns the panel.");
        }

        [TestMethod]
        public void InspectorActionButtonsReachTheFilesScreen()
        {
            using Harness harness = new();

            // Both actions operate on the selection, so there has to be one.
            harness.Files.SelectedRows.Add(new FileRowViewModel(
                new R2BrowserItem { Key = "assets/a.png", DisplayName = "a.png", Size = 10 }));

            // BrowseForFolder returns null in the harness, so Download stops before any I/O.
            // What is asserted is that the request arrived at all.
            harness.Inspector.DownloadCommand.Execute(null);
            Assert.AreEqual(1, harness.Dialogs.BrowseForFolderCalls, "Download must reach the Files screen.");

            harness.Inspector.CopyKeysCommand.Execute(null);
            Assert.AreEqual("assets/a.png", harness.Clipboard.LastText, "Copy key must reach the Files screen.");
        }

        [TestMethod]
        public void PreviewDownloadAndRetryReachTheFilesScreen()
        {
            using Harness harness = new();

            harness.Files.SelectedRows.Add(new FileRowViewModel(
                new R2BrowserItem { Key = "assets/a.png", DisplayName = "a.png", Size = 10 }));

            harness.Inspector.Preview.DownloadCommand.Execute(null);
            Assert.AreEqual(1, harness.Dialogs.BrowseForFolderCalls, "Preview download must reach the Files screen.");

            // Retry re-runs the inspector load for whatever is selected now, so the panel
            // ends up describing that object again rather than staying on the error state.
            harness.Inspector.Clear();
            Assert.IsFalse(harness.Inspector.HasSelection);

            harness.Inspector.Preview.RetryCommand.Execute(null);

            Assert.IsTrue(harness.Inspector.HasSelection, "Retry must reload the current selection.");
            Assert.AreEqual("a.png", harness.Inspector.Title);
        }

        [TestMethod]
        public void EveryInspectorEventHasASubscriberOnceTheFilesScreenIsBuilt()
        {
            using Harness harness = new();

            Assert.IsTrue(HasSubscriber(harness.Inspector, "CloseRequested"));
            Assert.IsTrue(HasSubscriber(harness.Inspector, "ActionRequested"));
            Assert.IsTrue(HasSubscriber(harness.Inspector.Preview, "DownloadRequested"));
            Assert.IsTrue(HasSubscriber(harness.Inspector.Preview, "RetryRequested"));
        }

        /// <summary>
        /// Reads the compiler-generated backing field for an event. A null delegate means the
        /// event is raised into the void, which is exactly the defect being guarded against.
        /// </summary>
        private static bool HasSubscriber(object target, string eventName)
        {
            System.Reflection.FieldInfo? field = target.GetType().GetField(
                eventName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            Assert.IsNotNull(field, "No backing field was found for the event " + eventName + ".");
            return field!.GetValue(target) is Delegate handler && handler.GetInvocationList().Length > 0;
        }

        private sealed class Harness : IDisposable
        {
            private readonly string _cacheRoot;
            private readonly PreviewCacheService _cache;

            public Harness()
            {
                _cacheRoot = Path.Combine(Path.GetTempPath(), "r2uploader-wpf-tests", Guid.NewGuid().ToString("N"));
                _cache = new PreviewCacheService(_cacheRoot);

                Session = new StubSession();
                Clipboard = new RecordingClipboard();
                Dialogs = new RecordingDialogService();

                StubLauncher launcher = new();
                StubToasts toasts = new();
                StubLog log = new();

                Preview = new PreviewViewModel(new R2PreviewService(_cache), launcher, toasts, Session, log);

                Inspector = new FileInspectorViewModel(
                    Session, Clipboard, launcher, toasts, log, new R2ObjectDetailsService(), Preview);

                Files = new FilesViewModel(
                    Session,
                    Dialogs,
                    toasts,
                    Clipboard,
                    launcher,
                    log,
                    new R2ObjectBrowserService(log),
                    new R2ObjectOperationService(log, new UploadStateStore(log, Path.Combine(_cacheRoot, "state"))),
                    new R2BulkOperationService(log),
                    new R2PresignedUrlService(),
                    new StubActivityHistory(),
                    Inspector);
            }

            public StubSession Session { get; }

            public RecordingClipboard Clipboard { get; }

            public RecordingDialogService Dialogs { get; }

            public PreviewViewModel Preview { get; }

            public FileInspectorViewModel Inspector { get; }

            public FilesViewModel Files { get; }

            public void Dispose()
            {
                Files.Dispose();
                _cache.Dispose();

                try { if (Directory.Exists(_cacheRoot)) Directory.Delete(_cacheRoot, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        // ------------------------------------------------------------------------- stubs

        internal sealed class StubSession : IAppSession
        {
            private AppSettings _settings;
            private readonly Dictionary<string, R2Credentials> _credentials = new(StringComparer.OrdinalIgnoreCase);

            public StubSession()
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
            public IReadOnlyList<R2BucketProfile> Profiles => _settings.BucketProfiles.Select(p => p.Clone()).ToList();
            public bool IsConfigured => true;

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

        internal sealed class RecordingClipboard : IClipboardService
        {
            public string? LastText { get; private set; }

            public void SetText(string text) => LastText = text;
        }

        internal sealed class RecordingDialogService : IDialogService
        {
            public int BrowseForFolderCalls { get; private set; }

            public string? BrowseForFolder(string title, string? initialDirectory = null)
            {
                BrowseForFolderCalls++;
                return null;
            }

            public IReadOnlyList<string> BrowseForFiles(string title) => Array.Empty<string>();

            public string? BrowseForSaveFile(string title, string suggestedFileName, string filter) => null;

            public Task ShowMessageAsync(string title, string message, string? details = null) => Task.CompletedTask;

            public Task ShowErrorAsync(string title, string message, string? technicalDetails = null) => Task.CompletedTask;

            public Task<bool> ConfirmAsync(string title, string message, string confirmLabel, bool isDestructive = false) =>
                Task.FromResult(false);

            public Task<ConfirmationResult> ConfirmAsync(
                string title, string message, string confirmLabel, string optionLabel, bool isDestructive = false) =>
                Task.FromResult(new ConfirmationResult(false, false));

            public Task<string?> PromptForTextAsync(TextPromptRequest request) => Task.FromResult<string?>(null);

            public Task<OverwriteDecision> AskOverwriteAsync(
                string objectKey, long existingLength, DateTime existingModifiedUtc, long localLength) =>
                Task.FromResult(OverwriteDecision.Skip);

            public Task<bool?> AskKeepPartsOnCancelAsync(int activeMultipartCount) => Task.FromResult<bool?>(null);

            public Task ShowTemporaryLinkAsync(TemporaryLinkDialogViewModel viewModel) => Task.CompletedTask;

            public Task<bool> ShowSettingsAsync(string? initialSection = null) => Task.FromResult(false);
        }

        internal sealed class StubActivityHistory : IActivityHistoryService
        {
            public event EventHandler<ActivityRecordedEventArgs>? Recorded;
            public event EventHandler? Cleared;

            public Task RecordAsync(ActivityRecord record, CancellationToken cancellationToken = default)
            {
                Recorded?.Invoke(this, new ActivityRecordedEventArgs(record));
                return Task.CompletedTask;
            }

            public Task<IReadOnlyList<ActivityRecord>> QueryAsync(
                ActivityQuery query, CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<ActivityRecord>>(Array.Empty<ActivityRecord>());

            public Task<ActivitySummary> SummarizeAsync(
                ActivityQuery query, CancellationToken cancellationToken = default) =>
                Task.FromResult(new ActivitySummary());

            public Task<bool> ClearAsync(CancellationToken cancellationToken = default)
            {
                Cleared?.Invoke(this, EventArgs.Empty);
                return Task.FromResult(true);
            }

            public Task<int> PruneAsync(int retentionDays, CancellationToken cancellationToken = default) =>
                Task.FromResult(0);
        }

        internal sealed class StubLauncher : IProcessLauncher
        {
            public void Open(string target) { }

            public void Start(string executable, string arguments) { }
        }

        internal sealed class StubToasts : IToastService
        {
            public System.Collections.ObjectModel.ReadOnlyObservableCollection<ToastViewModel> Toasts { get; } =
                new(new System.Collections.ObjectModel.ObservableCollection<ToastViewModel>());

            public void ShowSuccess(string message) { }

            public void ShowError(string message, string? actionLabel = null, Action? action = null) { }

            public ToastViewModel ShowProgress(string message) =>
                new(ToastKind.Progress, message, _ => { });

            public void Dismiss(ToastViewModel toast) { }
        }

        internal sealed class StubLog : ILoggingService
        {
            public string LogDirectory => Path.GetTempPath();

            public void Debug(string operation, string message) { }

            public void Info(string operation, string message) { }

            public void Warning(string operation, string message) { }

            public void Error(string operation, string message, Exception exception) { }

            public void HttpFailure(
                string operation, int statusCode, string awsErrorCode, string message, string objectKey, string uploadId) { }
        }
    }
}
