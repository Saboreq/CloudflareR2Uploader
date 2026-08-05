using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using CloudflareR2Uploader.Utilities;
using CloudflareR2Uploader.Wpf.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CloudflareR2Uploader.Wpf.ViewModels.Activity
{
    public sealed record ActivityFilterChip(string DisplayName, ActivityCategory? Category, bool ErrorsOnly)
    {
        public override string ToString() => DisplayName;
    }

    public sealed record DateRangeChoice(string DisplayName, int? Days)
    {
        public override string ToString() => DisplayName;
    }

    /// <summary>One row of the Activity table.</summary>
    public sealed class ActivityRowViewModel
    {
        public ActivityRowViewModel(ActivityRecord record)
        {
            Record = record;

            ActionText = DescribeAction(record.Action);
            IconKey = ResolveIconKey(record.Action);

            ObjectText = string.IsNullOrEmpty(record.SecondaryTarget)
                ? record.Target
                : record.Target + " → " + record.SecondaryTarget;

            if (record.IsError && !string.IsNullOrEmpty(record.ErrorMessage))
            {
                ObjectText = record.Target + " — " +
                             (string.IsNullOrEmpty(record.ErrorCode) ? string.Empty : record.ErrorCode + " ") +
                             record.ErrorMessage;
            }

            BucketText = record.BucketName;
            SizeText = record.SizeBytes.HasValue ? FileSizeFormatter.Format(record.SizeBytes.Value) : "—";

            DurationText = record.DurationMilliseconds.HasValue
                ? (record.DurationMilliseconds.Value / 1000d).ToString("0.0", CultureInfo.CurrentCulture) + "s"
                : "—";

            TimeText = record.TimestampUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
            ResultText = DescribeResult(record.Result);
            IsError = record.IsError;
            AutomationName = ActionText + " " + ObjectText + ", " + ResultText + ", " + TimeText;
        }

        public ActivityRecord Record { get; }

        public string ActionText { get; }

        public string IconKey { get; }

        public string ObjectText { get; }

        public string BucketText { get; }

        public string SizeText { get; }

        public string DurationText { get; }

        public string TimeText { get; }

        public string ResultText { get; }

        public bool IsError { get; }

        public string AutomationName { get; }

        private static string DescribeAction(ActivityAction action) => action switch
        {
            ActivityAction.NewFolder => "New folder",
            ActivityAction.PublicLink => "Public link",
            ActivityAction.TemporaryLink => "Temporary link",
            _ => action.ToString()
        };

        private static string DescribeResult(ActivityResult result) => result switch
        {
            ActivityResult.Success => "Success",
            ActivityResult.Failed => "Failed",
            ActivityResult.Cancelled => "Cancelled",
            ActivityResult.Skipped => "Skipped",
            ActivityResult.Copied => "Copied",
            ActivityResult.Created => "Created",
            _ => result.ToString()
        };

        private static string ResolveIconKey(ActivityAction action) => action switch
        {
            ActivityAction.Upload => "Icon.ArrowUp",
            ActivityAction.Download => "Icon.ArrowDown",
            ActivityAction.Delete => "Icon.Trash",
            ActivityAction.Rename => "Icon.Pencil",
            ActivityAction.Move => "Icon.Move",
            ActivityAction.Copy => "Icon.Copy",
            ActivityAction.NewFolder => "Icon.NewFolder",
            ActivityAction.PublicLink => "Icon.Link",
            ActivityAction.TemporaryLink => "Icon.Clock",
            ActivityAction.Connection => "Icon.Alert",
            _ => "Icon.Info"
        };
    }

    /// <summary>A date group header plus its rows, matching "TODAY · 4 AUGUST 2026".</summary>
    public sealed class ActivityDayGroup
    {
        public ActivityDayGroup(DateTime localDate, IReadOnlyList<ActivityRowViewModel> rows)
        {
            Rows = rows;

            DateTime today = DateTime.Now.Date;
            string relative = localDate == today
                ? "TODAY"
                : localDate == today.AddDays(-1) ? "YESTERDAY" : localDate.ToString("dddd", CultureInfo.CurrentCulture).ToUpperInvariant();

            Header = relative + " · " + localDate.ToString("d MMMM yyyy", CultureInfo.CurrentCulture).ToUpperInvariant();

            int events = rows.Count;
            long transferred = rows
                .Where(static row => row.Record.SizeBytes.HasValue &&
                                     row.Record.Action is ActivityAction.Upload or ActivityAction.Download)
                .Sum(static row => row.Record.SizeBytes!.Value);

            int errors = rows.Count(static row => row.IsError);

            Summary = events.ToString(CultureInfo.CurrentCulture) + " events · " +
                      FileSizeFormatter.Format(transferred) + " transferred" +
                      (errors > 0 ? " · " + errors.ToString(CultureInfo.CurrentCulture) + " error" + (errors == 1 ? string.Empty : "s") : string.Empty);
        }

        public string Header { get; }

        public string Summary { get; }

        public IReadOnlyList<ActivityRowViewModel> Rows { get; }
    }

    /// <summary>
    /// The Activity screen, implementing Images/05-activity-history.png against the real
    /// <see cref="IActivityHistoryService"/>. Nothing on this screen is sample data.
    /// </summary>
    public sealed partial class ActivityViewModel : ObservableObject, IDisposable
    {
        private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(250);

        private readonly IActivityHistoryService _history;
        private readonly IAppSession _session;
        private readonly IDialogService _dialogs;
        private readonly IToastService _toasts;
        private readonly ILoggingService _log;
        private readonly DispatcherTimer _searchTimer;

        private CancellationTokenSource? _queryCancellation;
        private bool _disposed;

        public ActivityViewModel(
            IActivityHistoryService history,
            IAppSession session,
            IDialogService dialogs,
            IToastService toasts,
            ILoggingService log)
        {
            _history = history;
            _session = session;
            _dialogs = dialogs;
            _toasts = toasts;
            _log = log;

            Groups = new ReadOnlyObservableCollection<ActivityDayGroup>(_groups);
            _selectedFilter = Filters[0];
            _selectedRange = DateRanges[1];

            _searchTimer = new DispatcherTimer { Interval = SearchDebounce };
            _searchTimer.Tick += (sender, args) =>
            {
                _searchTimer.Stop();
                _ = ReloadAsync();
            };

            _history.Recorded += OnRecorded;
            _history.Cleared += OnCleared;
        }

        private readonly ObservableCollection<ActivityDayGroup> _groups = new();

        public ReadOnlyObservableCollection<ActivityDayGroup> Groups { get; }

        public IReadOnlyList<ActivityFilterChip> Filters { get; } = new[]
        {
            new ActivityFilterChip("All", null, false),
            new ActivityFilterChip("Uploads", ActivityCategory.Uploads, false),
            new ActivityFilterChip("Downloads", ActivityCategory.Downloads, false),
            new ActivityFilterChip("Changes", ActivityCategory.Changes, false),
            new ActivityFilterChip("Errors", null, true)
        };

        public IReadOnlyList<DateRangeChoice> DateRanges { get; } = new[]
        {
            new DateRangeChoice("Today", 1),
            new DateRangeChoice("Last 7 days", 7),
            new DateRangeChoice("Last 30 days", 30),
            new DateRangeChoice("All time", null)
        };

        [ObservableProperty]
        private ActivityFilterChip _selectedFilter;

        [ObservableProperty]
        private DateRangeChoice _selectedRange;

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private int _errorCount;

        [ObservableProperty]
        private int _unreadCount;

        [ObservableProperty]
        private string _footerCounts = "0 events";

        [ObservableProperty]
        private string _footerTransferred = "0 B";

        [ObservableProperty]
        private string _retentionText = "History kept for 30 days";

        public bool IsEmpty => _groups.Count == 0;

        // -------------------------------------------------------------------- page hooks

        public void OnActivated()
        {
            UnreadCount = 0;
            _ = ReloadAsync();
        }

        public void OnProfileChanged() => _ = ReloadAsync();

        /// <summary>Prunes on startup so an old file cannot grow past its retention window.</summary>
        public async Task InitializeAsync()
        {
            await _history.PruneAsync(_session.Settings.ActivityRetentionDays).ConfigureAwait(true);
            await ReloadAsync().ConfigureAwait(true);
        }

        // ---------------------------------------------------------------------- commands

        [RelayCommand]
        private async Task RefreshAsync() => await ReloadAsync().ConfigureAwait(true);

        [RelayCommand]
        private async Task ExportCsvAsync()
        {
            string suggested = "cloudflare-r2-activity-" +
                               DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".csv";

            string? path = _dialogs.BrowseForSaveFile(
                "Export activity history", suggested, "CSV file (*.csv)|*.csv|All files (*.*)|*.*");

            if (string.IsNullOrEmpty(path)) return;

            try
            {
                IReadOnlyList<ActivityRecord> records = await _history
                    .QueryAsync(BuildQuery())
                    .ConfigureAwait(true);

                string csv = ActivityCsvExporter.Export(records);

                // A UTF-8 BOM is what makes Excel read non-ASCII object keys correctly.
                await File.WriteAllTextAsync(path, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true))
                    .ConfigureAwait(true);

                _toasts.ShowSuccess(records.Count.ToString(CultureInfo.CurrentCulture) + " events exported");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await _dialogs.ShowErrorAsync(
                    "The export could not be written",
                    LoggingService.Sanitize(ex.Message)).ConfigureAwait(true);
            }
        }

        [RelayCommand]
        private async Task ClearHistoryAsync()
        {
            bool confirmed = await _dialogs.ConfirmAsync(
                "Clear the activity history?",
                "This permanently removes every recorded event from this computer. It does not change anything in the bucket.",
                "Clear history",
                isDestructive: true).ConfigureAwait(true);

            if (!confirmed) return;

            await _history.ClearAsync().ConfigureAwait(true);
            await ReloadAsync().ConfigureAwait(true);
            _toasts.ShowSuccess("Activity history cleared");
        }

        // ----------------------------------------------------------------------- loading

        private ActivityQuery BuildQuery()
        {
            DateTime? from = SelectedRange.Days.HasValue
                ? DateTime.UtcNow.Date.AddDays(-(SelectedRange.Days.Value - 1))
                : null;

            return new ActivityQuery
            {
                Category = SelectedFilter.Category,
                ErrorsOnly = SelectedFilter.ErrorsOnly,
                FromUtc = from,
                SearchText = SearchText
            };
        }

        private async Task ReloadAsync()
        {
            CancellationTokenSource? previous = Interlocked.Exchange(ref _queryCancellation, new CancellationTokenSource());
            if (previous is not null)
            {
                try { previous.Cancel(); } catch (ObjectDisposedException) { }
                previous.Dispose();
            }

            CancellationTokenSource? current = _queryCancellation;
            if (current is null) return;

            IsLoading = true;

            try
            {
                ActivityQuery query = BuildQuery();

                IReadOnlyList<ActivityRecord> records = await _history
                    .QueryAsync(query, current.Token)
                    .ConfigureAwait(true);

                ActivitySummary summary = await _history
                    .SummarizeAsync(query, current.Token)
                    .ConfigureAwait(true);

                ActivitySummary errors = await _history
                    .SummarizeAsync(new ActivityQuery { ErrorsOnly = true, FromUtc = query.FromUtc }, current.Token)
                    .ConfigureAwait(true);

                _groups.Clear();
                foreach (var group in records
                             .GroupBy(static record => record.TimestampUtc.ToLocalTime().Date)
                             .OrderByDescending(static group => group.Key))
                {
                    _groups.Add(new ActivityDayGroup(
                        group.Key,
                        group.Select(static record => new ActivityRowViewModel(record)).ToList()));
                }

                FooterCounts = string.Format(
                    CultureInfo.CurrentCulture,
                    "{0} event{1}   |   {2} succeeded   |   {3} failed",
                    summary.EventCount, summary.EventCount == 1 ? string.Empty : "s",
                    summary.SucceededCount, summary.FailedCount);

                FooterTransferred = FileSizeFormatter.Format(summary.TransferredBytes);
                ErrorCount = errors.EventCount;

                RetentionText = "History kept for " +
                                _session.Settings.ActivityRetentionDays.ToString(CultureInfo.CurrentCulture) + " days";

                OnPropertyChanged(nameof(IsEmpty));
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer query.
            }
            catch (Exception ex)
            {
                _log?.Warning("Activity.Load", "The activity history could not be read (" + ex.GetType().Name + ").");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void OnRecorded(object? sender, ActivityRecordedEventArgs e)
        {
            System.Windows.Application? application = System.Windows.Application.Current;
            if (application is null) return;

            application.Dispatcher.BeginInvoke(new Action(() =>
            {
                UnreadCount++;
                _ = ReloadAsync();
            }));
        }

        private void OnCleared(object? sender, EventArgs e)
        {
            System.Windows.Application? application = System.Windows.Application.Current;
            application?.Dispatcher.BeginInvoke(new Action(() => UnreadCount = 0));
        }

        partial void OnSelectedFilterChanged(ActivityFilterChip value) => _ = ReloadAsync();

        partial void OnSelectedRangeChanged(DateRangeChoice value) => _ = ReloadAsync();

        partial void OnSearchTextChanged(string value)
        {
            _searchTimer.Stop();
            _searchTimer.Start();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _searchTimer.Stop();
            _history.Recorded -= OnRecorded;
            _history.Cleared -= OnCleared;

            CancellationTokenSource? query = Interlocked.Exchange(ref _queryCancellation, null);
            if (query is not null)
            {
                try { query.Cancel(); } catch (ObjectDisposedException) { }
                query.Dispose();
            }
        }
    }
}
