using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using CloudflareR2Uploader.Utilities;
using CloudflareR2Uploader.Wpf.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CloudflareR2Uploader.Wpf.ViewModels.Upload
{
    public sealed record OverwriteChoice(string DisplayName, OverwriteBehavior Behavior)
    {
        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// The Upload screen, implementing Images/04-upload-queue.png.
    /// <para>
    /// Owns no transfer logic: the shared <see cref="UploadQueueService"/> runs the queue
    /// exactly as it does for the WinForms front end, including multipart resume, the pause
    /// gate, retry policy and overwrite prompts. This class projects that state onto the
    /// design and translates the toolbar into service calls.
    /// </para>
    /// </summary>
    public sealed partial class UploadViewModel : ObservableObject, IDisposable
    {
        /// <summary>
        /// One timer refreshes every row and the overall figures. Binding the raw progress
        /// events would post thousands of dispatcher operations per second during a
        /// multipart upload.
        /// </summary>
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(200);

        private readonly IAppSession _session;
        private readonly IDialogService _dialogs;
        private readonly IToastService _toasts;
        private readonly IClipboardService _clipboard;
        private readonly ILoggingService _log;
        private readonly UploadQueueService _queue;
        private readonly IActivityHistoryService _activity;
        private readonly DispatcherTimer _refreshTimer;
        private readonly Dictionary<Guid, UploadQueueItemViewModel> _rowsByItem = new();

        private bool _disposed;

        public UploadViewModel(
            IAppSession session,
            IDialogService dialogs,
            IToastService toasts,
            IClipboardService clipboard,
            ILoggingService log,
            UploadQueueService queue,
            IActivityHistoryService activity)
        {
            _session = session;
            _dialogs = dialogs;
            _toasts = toasts;
            _clipboard = clipboard;
            _log = log;
            _queue = queue;
            _activity = activity;

            Rows = new ReadOnlyObservableCollection<UploadQueueItemViewModel>(_rows);

            AppSettings settings = _session.Settings;
            _destinationPrefix = settings.KeyPrefix;
            _preserveFolderStructure = settings.PreserveFolderStructure;
            _selectedOverwriteChoice = OverwriteChoices.First(
                choice => choice.Behavior == settings.OverwriteBehavior);

            _queue.SettingsProvider = BuildUploadSettings;
            _queue.CredentialsProvider = () => _session.Credentials;
            _queue.OverwritePrompt = new DialogOverwritePrompt(_dialogs);

            _queue.ItemAdded += OnItemAdded;
            _queue.ItemRemoved += OnItemRemoved;
            _queue.ItemFinished += OnItemFinished;
            _queue.RunStateChanged += OnRunStateChanged;

            _refreshTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = RefreshInterval };
            _refreshTimer.Tick += (sender, args) => RefreshAll();

            UpdateOverall();
        }

        private readonly ObservableCollection<UploadQueueItemViewModel> _rows = new();

        public ReadOnlyObservableCollection<UploadQueueItemViewModel> Rows { get; }

        public IReadOnlyList<OverwriteChoice> OverwriteChoices { get; } = new[]
        {
            new OverwriteChoice("Ask each time", OverwriteBehavior.Ask),
            new OverwriteChoice("Overwrite", OverwriteBehavior.Overwrite),
            new OverwriteChoice("Skip", OverwriteBehavior.Skip),
            new OverwriteChoice("Keep both", OverwriteBehavior.Rename)
        };

        // ------------------------------------------------------------------- destination

        [ObservableProperty]
        private string _destinationPrefix = string.Empty;

        /// <summary>Only honoured when exactly one file is queued, matching the WinForms rule.</summary>
        [ObservableProperty]
        private string _objectName = string.Empty;

        [ObservableProperty]
        private OverwriteChoice _selectedOverwriteChoice;

        [ObservableProperty]
        private bool _preserveFolderStructure = true;

        // ----------------------------------------------------------------------- overall

        [ObservableProperty]
        private double _overallPercent;

        [ObservableProperty]
        private string _overallTransferredText = "0 B / 0 B";

        [ObservableProperty]
        private string _elapsedText = "0:00";

        [ObservableProperty]
        private string _remainingText = "—";

        [ObservableProperty]
        private string _countsText = string.Empty;

        [ObservableProperty]
        private string _statusBarText = string.Empty;

        [ObservableProperty]
        private string _concurrencyText = string.Empty;

        [ObservableProperty]
        private int _queueCount;

        [ObservableProperty]
        private bool _isRunning;

        [ObservableProperty]
        private bool _isPaused;

        [ObservableProperty]
        private bool _isDragOver;

        public bool HasItems => _rows.Count > 0;

        public bool CanUploadAll => !IsRunning && _rows.Any(static row => row.Status == UploadItemStatus.Queued);

        public bool CanPause => IsRunning;

        public bool CanCancel => IsRunning;

        public bool CanRetryFailed => _rows.Any(static row => row.CanRetry);

        public bool CanClearCompleted => _rows.Any(static row => row.Status is UploadItemStatus.Completed or UploadItemStatus.Skipped);

        // ------------------------------------------------------------------- page hooks

        public void OnActivated() => RefreshAll();

        public void OnProfileChanged()
        {
            // The queue is deliberately not rebuilt: switching profile mid-run would orphan
            // in-flight multipart uploads. The destination line simply re-reads the profile.
            UpdateOverall();
        }

        public void OnConnectionSettingsChanged()
        {
            AppSettings settings = _session.Settings;
            DestinationPrefix = settings.KeyPrefix;
            PreserveFolderStructure = settings.PreserveFolderStructure;
            SelectedOverwriteChoice = OverwriteChoices.First(choice => choice.Behavior == settings.OverwriteBehavior);
            UpdateOverall();
        }

        // -------------------------------------------------------------------- add files

        [RelayCommand]
        private void BrowseFiles()
        {
            IReadOnlyList<string> files = _dialogs.BrowseForFiles("Choose files to upload");
            if (files.Count > 0) AddPaths(files);
        }

        [RelayCommand]
        private void BrowseFolder()
        {
            string? folder = _dialogs.BrowseForFolder("Choose a folder to upload");
            if (!string.IsNullOrEmpty(folder)) AddPaths(new[] { folder });
        }

        /// <summary>Entry point for drag and drop. Folders are walked by the shared queue service.</summary>
        public void AddPaths(IEnumerable<string> paths)
        {
            AddFilesResult result = _queue.AddPaths(paths, BuildUploadSettings());

            if (result.Added == 0 && result.Duplicates > 0)
            {
                _toasts.ShowSuccess("Already queued");
            }
            else if (result.Added > 0)
            {
                _toasts.ShowSuccess(result.Added == 1
                    ? "1 file added to the queue"
                    : result.Added.ToString(CultureInfo.CurrentCulture) + " files added to the queue");
            }

            if (result.Inaccessible.Count > 0 || result.Unreadable.Count > 0)
            {
                int skipped = result.Inaccessible.Count + result.Unreadable.Count;
                _toasts.ShowError(skipped == 1
                    ? "1 item could not be read and was skipped"
                    : skipped.ToString(CultureInfo.CurrentCulture) + " items could not be read and were skipped");
            }

            RefreshAll();
        }

        // ------------------------------------------------------------------ run control

        [RelayCommand]
        private void UploadAll()
        {
            if (!_session.IsConfigured)
            {
                _toasts.ShowError("Configure a bucket and credentials in Settings before uploading.");
                return;
            }

            _queue.RecomputeKeys(BuildUploadSettings(), ObjectName);
            _queue.StartAsync();
            _refreshTimer.Start();
            RefreshAll();
        }

        [RelayCommand]
        private void TogglePause()
        {
            if (_queue.IsPaused) _queue.Resume();
            else _queue.Pause();

            RefreshAll();
        }

        [RelayCommand]
        private async Task CancelAsync()
        {
            AppSettings settings = BuildUploadSettings();
            int activeMultipart = _queue.GetItems().Count(
                item => !item.Status.IsTerminal() && item.WillUseMultipart(settings.MultipartThresholdBytes));

            bool preserveParts = true;
            if (activeMultipart > 0)
            {
                bool? keep = await _dialogs.AskKeepPartsOnCancelAsync(activeMultipart).ConfigureAwait(true);
                if (keep is null) return;
                preserveParts = keep.Value;
            }

            _queue.CancelAll(preserveParts);
            RefreshAll();
        }

        [RelayCommand]
        private void RetryFailed()
        {
            int count = _queue.RetryFailed();
            if (count == 0) return;

            if (!_queue.IsRunning) _queue.StartAsync();
            _refreshTimer.Start();
            RefreshAll();
        }

        [RelayCommand]
        private void ClearCompleted()
        {
            _queue.ClearCompleted();
            RefreshAll();
        }

        // --------------------------------------------------------------- per-row actions

        private void PauseOrResumeRow(UploadQueueItemViewModel row)
        {
            // The pause gate is queue-wide: a single file cannot be suspended independently
            // without abandoning its in-flight parts, which would lose their ETags.
            TogglePause();
        }

        private void CancelRow(UploadQueueItemViewModel row)
        {
            _queue.CancelItem(row.Item);
            RefreshAll();
        }

        private void RetryRow(UploadQueueItemViewModel row)
        {
            _queue.RetryItem(row.Item);
            if (!_queue.IsRunning) _queue.StartAsync();
            _refreshTimer.Start();
            RefreshAll();
        }

        private void RemoveRow(UploadQueueItemViewModel row)
        {
            _queue.Remove(row.Item);
            RefreshAll();
        }

        private void CopyRowLink(UploadQueueItemViewModel row)
        {
            string? url = row.Item.PublicUrl;
            if (string.IsNullOrEmpty(url)) return;

            _clipboard.SetText(url);
            _toasts.ShowSuccess("Link copied to clipboard");
        }

        // -------------------------------------------------------------- queue plumbing

        private void OnItemAdded(object? sender, QueueItemEventArgs e) => Post(() => AddRow(e.Item));

        private void OnItemRemoved(object? sender, QueueItemEventArgs e) => Post(() =>
        {
            if (!_rowsByItem.TryGetValue(e.Item.Id, out UploadQueueItemViewModel? row)) return;
            _rowsByItem.Remove(e.Item.Id);
            _rows.Remove(row);
            UpdateOverall();
        });

        private void OnRunStateChanged(object? sender, EventArgs e) => Post(RefreshAll);

        private void OnItemFinished(object? sender, UploadFinishedEventArgs e) => Post(async () =>
        {
            RefreshAll();

            UploadResult result = e.Result;
            if (result.Outcome is UploadOutcome.Paused) return;

            AppSettings settings = _session.Settings;

            ActivityResult activityResult = result.Outcome switch
            {
                UploadOutcome.Succeeded => ActivityResult.Success,
                UploadOutcome.Failed => ActivityResult.Failed,
                UploadOutcome.Skipped => ActivityResult.Skipped,
                _ => ActivityResult.Cancelled
            };

            ActivityRecord record = new()
            {
                Action = ActivityAction.Upload,
                Result = activityResult,
                Target = result.ObjectKey ?? e.Item.FileName,
                BucketName = settings.BucketName,
                ProfileId = settings.ActiveBucketProfileId,
                SizeBytes = result.Outcome == UploadOutcome.Succeeded ? e.Item.FileSize : null,
                DurationMilliseconds = (long)e.Item.Elapsed.TotalMilliseconds,
                ErrorMessage = result.Outcome == UploadOutcome.Failed ? result.Message ?? string.Empty : string.Empty,
                TimestampUtc = DateTime.UtcNow
            };

            await _activity.RecordAsync(record).ConfigureAwait(true);
        });

        private void AddRow(UploadQueueItem item)
        {
            if (_rowsByItem.ContainsKey(item.Id)) return;

            UploadQueueItemViewModel row = new(
                item, PauseOrResumeRow, CancelRow, RetryRow, RemoveRow, CopyRowLink);

            _rowsByItem[item.Id] = row;
            _rows.Add(row);
            UpdateOverall();
        }

        private void RefreshAll()
        {
            foreach (UploadQueueItemViewModel row in _rows) row.Refresh();
            UpdateOverall();

            // Stop the timer once nothing can change on its own, so an idle Upload page
            // costs nothing.
            if (!_queue.IsRunning && _refreshTimer.IsEnabled) _refreshTimer.Stop();
        }

        private void UpdateOverall()
        {
            OverallProgressInfo overall = _queue.GetOverallProgress();
            AppSettings settings = _session.Settings;

            IsRunning = _queue.IsRunning;
            IsPaused = _queue.IsPaused;
            QueueCount = _rows.Count;

            OverallPercent = overall.Percent;
            OverallTransferredText = FileSizeFormatter.FormatProgress(overall.TransferredBytes, overall.TotalBytes);
            ElapsedText = DurationFormatter.Format(overall.Elapsed);
            RemainingText = DurationFormatter.FormatEta(overall.RemainingBytes, overall.BytesPerSecond);

            int paused = _rows.Count(static row => row.Status == UploadItemStatus.Paused);
            CountsText = string.Format(
                CultureInfo.CurrentCulture,
                "{0} done · {1} failed · {2} paused · {3} queued · {4} active",
                overall.Succeeded, overall.Failed, paused, overall.Queued, overall.Active);

            string prefix = ObjectKeyUtility.NormalizePrefix(DestinationPrefix);
            StatusBarText = string.Format(
                CultureInfo.CurrentCulture,
                "{0} in queue   |   destination {1} on {2}",
                _rows.Count,
                prefix.Length == 0 ? "bucket root" : prefix,
                string.IsNullOrWhiteSpace(settings.BucketName) ? "(no bucket)" : settings.BucketName);

            ConcurrencyText = string.Format(
                CultureInfo.CurrentCulture,
                "{0} parallel transfer{1} · {2} MB parts",
                settings.ParallelParts,
                settings.ParallelParts == 1 ? string.Empty : "s",
                settings.PartSizeMiB);

            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(CanUploadAll));
            OnPropertyChanged(nameof(CanPause));
            OnPropertyChanged(nameof(CanCancel));
            OnPropertyChanged(nameof(CanRetryFailed));
            OnPropertyChanged(nameof(CanClearCompleted));
        }

        /// <summary>
        /// Builds the settings snapshot the queue runs against, applying the destination the
        /// user typed on this screen without persisting it until Settings is saved.
        /// </summary>
        private AppSettings BuildUploadSettings()
        {
            AppSettings settings = _session.Settings;
            settings.KeyPrefix = ObjectKeyUtility.Normalize(DestinationPrefix);
            settings.PreserveFolderStructure = PreserveFolderStructure;
            settings.OverwriteBehavior = SelectedOverwriteChoice.Behavior;
            return settings;
        }

        private static void Post(Action action)
        {
            System.Windows.Application? application = System.Windows.Application.Current;
            if (application is null || application.Dispatcher.CheckAccess()) action();
            else application.Dispatcher.BeginInvoke(action);
        }

        partial void OnDestinationPrefixChanged(string value)
        {
            _queue.RecomputeKeys(BuildUploadSettings(), ObjectName);
            UpdateOverall();
        }

        partial void OnPreserveFolderStructureChanged(bool value) =>
            _queue.RecomputeKeys(BuildUploadSettings(), ObjectName);

        partial void OnObjectNameChanged(string value) =>
            _queue.RecomputeKeys(BuildUploadSettings(), value);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _refreshTimer.Stop();
            _queue.ItemAdded -= OnItemAdded;
            _queue.ItemRemoved -= OnItemRemoved;
            _queue.ItemFinished -= OnItemFinished;
            _queue.RunStateChanged -= OnRunStateChanged;
        }

        /// <summary>
        /// Bridges the shared queue's overwrite question to the WPF dialog. Kept private to
        /// the upload screen because it must run on the UI thread.
        /// </summary>
        private sealed class DialogOverwritePrompt : IOverwritePrompt
        {
            private readonly IDialogService _dialogs;

            public DialogOverwritePrompt(IDialogService dialogs) => _dialogs = dialogs;

            public Task<OverwriteDecision> AskAsync(UploadQueueItem item, ExistingObjectInfo existing)
            {
                System.Windows.Application? application = System.Windows.Application.Current;
                if (application is null)
                {
                    // Without a UI to ask with, skipping is the choice that cannot destroy data.
                    return Task.FromResult(OverwriteDecision.Skip);
                }

                return application.Dispatcher.InvokeAsync(
                    () => _dialogs.AskOverwriteAsync(
                        existing.Key, existing.Length, existing.LastModifiedUtc, item.FileSize)).Task.Unwrap();
            }
        }
    }
}
