using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CloudflareR2Uploader.Forms;
using CloudflareR2Uploader.Controls;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using CloudflareR2Uploader.Theming;

namespace CloudflareR2Uploader.Tray
{
    public sealed class TrayApplicationContext : ApplicationContext
    {
        private readonly MainForm _form;
        private readonly NotifyIcon _notifyIcon;
        private readonly SingleInstanceCoordinator _singleInstance;
        private readonly ToolStripMenuItem _openItem;
        private readonly ToolStripMenuItem _hideItem;
        private readonly ToolStripMenuItem _statusItem;
        private readonly ToolStripMenuItem _uploadAllItem;
        private readonly ToolStripMenuItem _pauseItem;
        private readonly ToolStripMenuItem _startWithWindowsItem;
        private readonly ILoggingService _log;
        private readonly UpdateService _updateService;
        private readonly string _updateManifestUrl;
        private readonly System.Windows.Forms.Timer _updateTimer;
        private bool _exiting;
        private bool _updateCheckInProgress;
        private DateTime _lastNotificationUtc = DateTime.MinValue;
        private bool _wasRunning;

        public TrayApplicationContext(ILoggingService log, SingleInstanceCoordinator singleInstance, InstanceLaunchCommand launchCommand)
        {
            _log = log;
            _singleInstance = singleInstance;
            _form = new MainForm(log);
            _form.HideToTrayRequested += OnHideToTrayRequested;
            _form.FormClosed += OnFormClosed;
            _form.Resize += OnFormResize;

            ContextMenuStrip menu = new ContextMenuStrip { BackColor = Theme.ElevatedBackground, ForeColor = Theme.TextPrimary, Renderer = new DarkContextMenuRenderer() };
            _openItem = Add(menu, "Open Cloudflare R2 Uploader", (s, e) => ShowWindow());
            _hideItem = Add(menu, "Hide window", (s, e) => _form.Hide());
            _statusItem = Add(menu, "No uploads running", null); _statusItem.Enabled = false;
            _uploadAllItem = Add(menu, "Upload all", (s, e) => _form.UploadAllFromTray());
            _pauseItem = Add(menu, "Pause uploads", (s, e) => _form.TogglePauseFromTray());
            Add(menu, "Open Files page", (s, e) => { ShowWindow(); _form.OpenFilesPage(); });
            Add(menu, "Settings", (s, e) => { ShowWindow(); _form.OpenSettingsDialog(); });
            Add(menu, "Check for updates...", async (s, e) => await CheckForUpdatesAsync(true));
            Add(menu, "Open logs folder", (s, e) => Process.Start(new ProcessStartInfo(log == null ? string.Empty : log.LogDirectory) { UseShellExecute = true }));
            _startWithWindowsItem = Add(menu, "Start with Windows", OnToggleStartup);
            menu.Items.Add(new ToolStripSeparator());
            Add(menu, "Exit", (s, e) => ExitApplication(false));
            menu.Opening += (s, e) => UpdateMenu();

            Icon icon = Program.LoadApplicationIcon();
            _notifyIcon = new NotifyIcon { Icon = icon ?? SystemIcons.Application, Text = "Cloudflare R2 Uploader", ContextMenuStrip = menu, Visible = true };
            _notifyIcon.DoubleClick += (s, e) => ShowWindow();
            _notifyIcon.BalloonTipClicked += (s, e) => ShowWindow();
            _singleInstance.ActivationRequested += OnActivationRequested;
            _form.BackgroundActivityChanged += OnBackgroundActivityChanged;
            _form.BackgroundOperationCompleted += OnBackgroundOperationCompleted;
            _singleInstance.StartListening();
            _wasRunning = _form.IsUploadRunning;

            _updateService = new UpdateService();
            _updateManifestUrl = UpdateService.LoadManifestUrl(AppDomain.CurrentDomain.BaseDirectory);
            _updateTimer = new System.Windows.Forms.Timer { Interval = 2500 };
            _updateTimer.Tick += async (s, e) =>
            {
                _updateTimer.Stop();
                if (_form.CurrentSettings.CheckForUpdatesAutomatically) await CheckForUpdatesAsync(false);
            };
            if (!string.IsNullOrEmpty(_updateManifestUrl)) _updateTimer.Start();

            if (launchCommand == InstanceLaunchCommand.Show && !_form.CurrentSettings.StartMinimized) _form.Show();
        }

        private async Task CheckForUpdatesAsync(bool userInitiated)
        {
            if (_updateCheckInProgress) return;
            if (string.IsNullOrEmpty(_updateManifestUrl))
            {
                if (userInitiated) MessageBox.Show(_form, "This build does not have an update channel configured.", "Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _updateCheckInProgress = true;
            try
            {
                UpdateCheckResult result = await _updateService.CheckAsync(_updateManifestUrl, Program.GetVersion(), CancellationToken.None);
                if (!result.IsUpdateAvailable)
                {
                    if (userInitiated) MessageBox.Show(_form, "Cloudflare R2 Uploader is up to date.", "Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                ShowWindow();
                using (UpdateAvailableDialog prompt = new UpdateAvailableDialog(Program.GetVersion(), result.Manifest))
                {
                    if (prompt.ShowDialog(_form) != DialogResult.Yes) return;
                }

                using (CancellationTokenSource cancellation = new CancellationTokenSource())
                using (UpdateDownloadDialog progressDialog = new UpdateDownloadDialog(cancellation))
                {
                    Progress<UpdateDownloadProgress> progress = new Progress<UpdateDownloadProgress>(progressDialog.Report);
                    progressDialog.Show(_form);
                    string installer;
                    try { installer = await _updateService.DownloadInstallerAsync(result.Manifest, progress, cancellation.Token); }
                    finally { progressDialog.Close(); }
                    if (!_form.RequestRealExit(false)) return;
                    Process.Start(new ProcessStartInfo(installer, "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /UPDATE=1") { UseShellExecute = true });
                    _exiting = true;
                    _notifyIcon.Visible = false;
                    _form.Close();
                }
            }
            catch (OperationCanceledException)
            {
                if (userInitiated) MessageBox.Show(_form, "The update download was cancelled.", "Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                if (_log != null) _log.Warning("Update.Check", "The update check or verified download could not be completed.");
                if (userInitiated) MessageBox.Show(_form, "The update check could not be completed.\r\n\r\n" + LoggingService.Sanitize(ex.Message), "Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { _updateCheckInProgress = false; }
        }

        private static ToolStripMenuItem Add(ContextMenuStrip menu, string text, EventHandler handler)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text) { ForeColor = Theme.TextPrimary };
            if (handler != null) item.Click += handler;
            menu.Items.Add(item);
            return item;
        }

        private void OnActivationRequested(object sender, EventArgs e) { if (_form.IsHandleCreated) _form.BeginInvoke(new Action(ShowWindow)); }
        private void ShowWindow()
        {
            if (_form.WindowState == FormWindowState.Minimized) _form.WindowState = FormWindowState.Normal;
            EnsureOnScreen();
            _form.Show();
            _form.BringToFront();
            _form.Activate();
        }
        private void EnsureOnScreen()
        {
            if (Screen.FromRectangle(_form.Bounds).WorkingArea.IntersectsWith(_form.Bounds)) return;
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            _form.Location = new Point(area.Left + Math.Max(0, (area.Width - _form.Width) / 2), area.Top + Math.Max(0, (area.Height - _form.Height) / 2));
        }
        private void OnHideToTrayRequested(object sender, EventArgs e)
        {
            _form.Hide();
            if (_form.CurrentSettings.ShowTrayNotifications && !_form.HasShownCloseToTrayNotice)
            {
                _form.HasShownCloseToTrayNotice = true;
                _notifyIcon.ShowBalloonTip(4000, "Still running", "Cloudflare R2 Uploader is continuing in the notification area. Use Exit from the tray menu to close it.", ToolTipIcon.Info);
            }
        }
        private void OnFormResize(object sender, EventArgs e) { if (_form.WindowState == FormWindowState.Minimized && _form.CurrentSettings.MinimizeToTray) _form.BeginInvoke(new Action(_form.Hide)); }
        private void UpdateMenu()
        {
            _openItem.Enabled = !_form.Visible;
            _hideItem.Visible = _form.Visible;
            _statusItem.Text = _form.GetUploadStatusText();
            _uploadAllItem.Enabled = _form.CanUploadAll;
            _pauseItem.Enabled = _form.IsUploadRunning;
            _pauseItem.Text = _form.IsUploadPaused ? "Resume uploads" : "Pause uploads";
            string executable = Application.ExecutablePath;
            _startWithWindowsItem.Checked = _form.StartupRegistration.IsEnabled(executable);
        }
        private void OnToggleStartup(object sender, EventArgs e)
        {
            string error;
            bool enable = !_form.StartupRegistration.IsEnabled(Application.ExecutablePath);
            if (!_form.StartupRegistration.SetEnabled(enable, Application.ExecutablePath, out error)) MessageBox.Show(_form, error, "Start with Windows", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        private void OnBackgroundActivityChanged(object sender, EventArgs e)
        {
            bool running = _form.IsUploadRunning;
            if (_wasRunning && !running && !_form.Visible && _form.CurrentSettings.ShowTrayNotifications && DateTime.UtcNow - _lastNotificationUtc >= TimeSpan.FromSeconds(10))
            {
                _lastNotificationUtc = DateTime.UtcNow;
                string status = _form.GetUploadStatusText();
                bool failed = status.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0;
                _notifyIcon.ShowBalloonTip(4500, failed ? "Uploads completed with failures" : "Uploads complete", status, failed ? ToolTipIcon.Warning : ToolTipIcon.Info);
            }
            _wasRunning = running;
            if (_notifyIcon.ContextMenuStrip != null) UpdateMenu();
        }
        private void OnBackgroundOperationCompleted(object sender, BackgroundOperationEventArgs e)
        {
            if (_form.Visible || !_form.CurrentSettings.ShowTrayNotifications || DateTime.UtcNow - _lastNotificationUtc < TimeSpan.FromSeconds(10)) return;
            _lastNotificationUtc = DateTime.UtcNow;
            _notifyIcon.ShowBalloonTip(4500, e.Failed ? "Operation needs attention" : "Operation complete", e.Message, e.Failed ? ToolTipIcon.Warning : ToolTipIcon.Info);
        }
        private void ExitApplication(bool shutdown)
        {
            if (_exiting) return;
            if (!_form.RequestRealExit(shutdown)) return;
            _exiting = true;
            _notifyIcon.Visible = false;
            _form.Close();
        }
        private void OnFormClosed(object sender, FormClosedEventArgs e)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _singleInstance.ActivationRequested -= OnActivationRequested;
            _form.BackgroundActivityChanged -= OnBackgroundActivityChanged;
            _form.BackgroundOperationCompleted -= OnBackgroundOperationCompleted;
            ExitThread();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { _updateTimer.Dispose(); _updateService.Dispose(); _notifyIcon.Dispose(); _form.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
