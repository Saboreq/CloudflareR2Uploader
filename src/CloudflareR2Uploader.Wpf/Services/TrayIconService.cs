using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CloudflareR2Uploader.Services;
using CloudflareR2Uploader.Wpf.ViewModels;
using CloudflareR2Uploader.Wpf.Views;
using Forms = System.Windows.Forms;

namespace CloudflareR2Uploader.Wpf.Services
{
    /// <summary>
    /// Owns the notification-area lifetime and translates tray actions into shell commands.
    /// The application remains alive while the window is hidden, which preserves the queue.
    /// </summary>
    public sealed class TrayIconService : IDisposable
    {
        private readonly MainWindow _window;
        private readonly ShellViewModel _shell;
        private readonly IAppSession _session;
        private readonly IDialogService _dialogs;
        private readonly IStartupRegistrationService _startup;
        private readonly IProcessLauncher _launcher;
        private readonly ILoggingService _log;
        private readonly UploadQueueService _queue;
        private readonly SingleInstanceCoordinator _singleInstance;
        private readonly IApplicationExitCoordinator _exitCoordinator;
        private readonly UpdateService _updates;

        private Forms.NotifyIcon? _icon;
        private Forms.ToolStripMenuItem? _openItem;
        private Forms.ToolStripMenuItem? _hideItem;
        private Forms.ToolStripMenuItem? _statusItem;
        private Forms.ToolStripMenuItem? _uploadItem;
        private Forms.ToolStripMenuItem? _pauseItem;
        private Forms.ToolStripMenuItem? _startupItem;
        private bool _started;
        private bool _exiting;
        private bool _shownCloseNotice;
        private bool _updateCheckInProgress;
        private bool _updateAvailable;
        private DispatcherTimer? _updateTimer;

        public TrayIconService(
            MainWindow window,
            ShellViewModel shell,
            IAppSession session,
            IDialogService dialogs,
            IStartupRegistrationService startup,
            IProcessLauncher launcher,
            ILoggingService log,
            UploadQueueService queue,
            SingleInstanceCoordinator singleInstance,
            IApplicationExitCoordinator exitCoordinator,
            UpdateService updates)
        {
            _window = window;
            _shell = shell;
            _session = session;
            _dialogs = dialogs;
            _startup = startup;
            _launcher = launcher;
            _log = log;
            _queue = queue;
            _singleInstance = singleInstance;
            _exitCoordinator = exitCoordinator;
            _updates = updates;
        }

        public void Start()
        {
            if (_started) return;
            _started = true;

            Forms.ContextMenuStrip menu = new();
            _openItem = Add(menu, "Open Cloudflare R2 Uploader", (sender, args) => ShowWindow());
            _hideItem = Add(menu, "Hide window", (sender, args) => _window.Hide());
            _statusItem = Add(menu, "No uploads running", null);
            _statusItem.Enabled = false;
            _uploadItem = Add(menu, "Upload all", (sender, args) => _shell.Upload.UploadAllCommand.Execute(null));
            _pauseItem = Add(menu, "Pause uploads", (sender, args) => _shell.Upload.TogglePauseCommand.Execute(null));
            Add(menu, "Open Upload page", (sender, args) => Navigate(ShellPage.Upload));
            Add(menu, "Open Files page", (sender, args) => Navigate(ShellPage.Files));
            Add(menu, "Open Settings", async (sender, args) => await OpenSettingsAsync().ConfigureAwait(true));
            Add(menu, "Check for updates...", async (sender, args) => await OpenSettingsAsync("updates").ConfigureAwait(true));
            Add(menu, "Open logs folder", (sender, args) => _launcher.Open(_log.LogDirectory));
            _startupItem = Add(menu, "Start with Windows", OnToggleStartup);
            menu.Items.Add(new Forms.ToolStripSeparator());
            Add(menu, "Exit", async (sender, args) => await ExitAsync().ConfigureAwait(true));
            menu.Opening += (sender, args) => UpdateMenu();

            _icon = new Forms.NotifyIcon
            {
                Icon = LoadIcon(),
                Text = "Cloudflare R2 Uploader",
                ContextMenuStrip = menu,
                Visible = true
            };
            _icon.DoubleClick += (sender, args) => ShowWindow();
            _icon.BalloonTipClicked += async (sender, args) =>
            {
                if (_updateAvailable) await OpenSettingsAsync("updates").ConfigureAwait(true);
                else ShowWindow();
            };

            _window.CloseRequestHandler = HandleWindowClose;
            _window.StateChanged += OnWindowStateChanged;
            _window.Closed += OnWindowClosed;
            _singleInstance.ActivationRequested += OnActivationRequested;
            _queue.RunStateChanged += OnQueueStateChanged;
            _exitCoordinator.ExitCommitted += OnExitCommitted;
            _singleInstance.StartListening();

            string? updateManifest = UpdateService.LoadManifestUrl(AppContext.BaseDirectory);
            if (!string.IsNullOrEmpty(updateManifest))
            {
                _updateTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(2500)
                };
                _updateTimer.Tick += OnUpdateTimerTick;
                _updateTimer.Start();
            }
        }

        public void ShowWindow()
        {
            if (!_window.Dispatcher.CheckAccess())
            {
                _window.Dispatcher.BeginInvoke(new Action(ShowWindow));
                return;
            }

            if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
            if (!_window.IsVisible) _window.Show();
            _window.ShowInTaskbar = true;
            _window.Activate();
            _window.Topmost = true;
            _window.Topmost = false;
            _window.Focus();
        }

        private bool HandleWindowClose()
        {
            if (_exiting) return true;

            if (_session.Settings.CloseToTray || _queue.IsRunning)
            {
                if (!_shownCloseNotice && _session.Settings.ShowTrayNotifications)
                {
                    _shownCloseNotice = true;
                    _icon?.ShowBalloonTip(
                        4000,
                        "Still running",
                        "Cloudflare R2 Uploader is continuing in the notification area. Use Exit from the tray menu to close it.",
                        Forms.ToolTipIcon.Info);
                }

                return false;
            }

            _exiting = true;
            return true;
        }

        private void OnWindowStateChanged(object? sender, EventArgs e)
        {
            if (_window.WindowState != WindowState.Minimized || !_session.Settings.MinimizeToTray) return;
            _window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                _window.ShowInTaskbar = false;
                _window.Hide();
            }));
        }

        private void Navigate(ShellPage page)
        {
            ShowWindow();
            _shell.NavigateCommand.Execute(page);
        }

        private async Task OpenSettingsAsync(string? initialSection = null)
        {
            ShowWindow();
            await _shell.ShowSettingsAsync(initialSection).ConfigureAwait(true);
        }

        private async void OnUpdateTimerTick(object? sender, EventArgs e)
        {
            _updateTimer?.Stop();
            if (!_session.Settings.CheckForUpdatesAutomatically || _updateCheckInProgress) return;

            string? manifestUrl = UpdateService.LoadManifestUrl(AppContext.BaseDirectory);
            if (string.IsNullOrEmpty(manifestUrl)) return;

            _updateCheckInProgress = true;
            try
            {
                Models.UpdateCheckResult result = await _updates
                    .CheckAsync(manifestUrl, ApplicationInfo.DisplayVersion, CancellationToken.None)
                    .ConfigureAwait(true);
                _updateAvailable = result.IsUpdateAvailable;
                if (_updateAvailable && _session.Settings.NotifyOnUpdateAvailable)
                {
                    _icon?.ShowBalloonTip(
                        6000,
                        "Update available",
                        "Cloudflare R2 Uploader " + result.Manifest.Version + " is ready to download.",
                        Forms.ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                _log.Warning("Update.Check", "The automatic update check failed (" + ex.GetType().Name + ").");
            }
            finally
            {
                _updateCheckInProgress = false;
            }
        }

        private async Task ExitAsync()
        {
            if (_exiting) return;

            if (!await _exitCoordinator.PrepareAsync().ConfigureAwait(true)) return;

            _exitCoordinator.Commit();
            if (_icon is not null) _icon.Visible = false;
            _window.Close();
            Application.Current.Shutdown();
        }

        private void OnToggleStartup(object? sender, EventArgs e)
        {
            string executable = Environment.ProcessPath ?? string.Empty;
            bool enable = !_startup.IsEnabled(executable);
            if (_startup.SetEnabled(enable, executable, out string? error)) return;
            _ = _dialogs.ShowErrorAsync("Startup setting could not be changed", error ?? "Windows rejected the startup registration.");
        }

        private void OnActivationRequested(object? sender, EventArgs e) => ShowWindow();

        private void OnExitCommitted(object? sender, EventArgs e)
        {
            _exiting = true;
            if (_icon is not null) _icon.Visible = false;
        }

        private void OnQueueStateChanged(object? sender, EventArgs e)
        {
            if (_icon?.ContextMenuStrip?.Visible == true) UpdateMenu();
        }

        private void UpdateMenu()
        {
            if (_openItem is null || _hideItem is null || _statusItem is null ||
                _uploadItem is null || _pauseItem is null || _startupItem is null) return;

            _openItem.Enabled = !_window.IsVisible;
            _hideItem.Visible = _window.IsVisible;
            _statusItem.Text = _shell.Upload.CountsText;
            _uploadItem.Enabled = _shell.Upload.CanUploadAll;
            _pauseItem.Enabled = _shell.Upload.CanPause;
            _pauseItem.Text = _shell.Upload.IsPaused ? "Resume uploads" : "Pause uploads";

            string executable = Environment.ProcessPath ?? string.Empty;
            _startupItem.Checked = _startup.IsEnabled(executable);
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            if (!_exiting) _exiting = true;
            if (_icon is not null) _icon.Visible = false;
            Application.Current.Shutdown();
        }

        private static Forms.ToolStripMenuItem Add(Forms.ContextMenuStrip menu, string text, EventHandler? handler)
        {
            Forms.ToolStripMenuItem item = new(text);
            if (handler is not null) item.Click += handler;
            menu.Items.Add(item);
            return item;
        }

        private static Icon LoadIcon()
        {
            try
            {
                string? executable = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(executable) && File.Exists(executable))
                {
                    Icon? associated = Icon.ExtractAssociatedIcon(executable);
                    if (associated is not null) return associated;
                }

                Assembly assembly = typeof(TrayIconService).Assembly;
                using Stream? stream = assembly.GetManifestResourceStream("CloudflareR2Uploader.Wpf.Resources.app.ico")
                                       ?? assembly.GetManifestResourceStream("CloudflareR2Uploader.Resources.app.ico");
                if (stream is not null) return new Icon(stream);
            }
            catch (Exception)
            {
                // Missing icon must never prevent the window from opening.
            }

            return SystemIcons.Application;
        }

        public void Dispose()
        {
            if (!_started) return;
            _started = false;

            _singleInstance.ActivationRequested -= OnActivationRequested;
            _queue.RunStateChanged -= OnQueueStateChanged;
            _exitCoordinator.ExitCommitted -= OnExitCommitted;
            _window.StateChanged -= OnWindowStateChanged;
            _window.Closed -= OnWindowClosed;
            _window.CloseRequestHandler = null;
            if (_updateTimer is not null)
            {
                _updateTimer.Stop();
                _updateTimer.Tick -= OnUpdateTimerTick;
                _updateTimer = null;
            }

            if (_icon is not null)
            {
                _icon.Visible = false;
                _icon.ContextMenuStrip?.Dispose();
                _icon.Dispose();
                _icon = null;
            }
        }
    }
}
