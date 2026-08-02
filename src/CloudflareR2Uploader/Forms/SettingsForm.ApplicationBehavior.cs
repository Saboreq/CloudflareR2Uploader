using System;
using System.Drawing;
using System.Windows.Forms;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using CloudflareR2Uploader.Theming;

namespace CloudflareR2Uploader.Forms
{
    public partial class SettingsForm
    {
        private IStartupRegistrationService _startupRegistration;
        private CheckBox _closeToTrayCheckBox;
        private CheckBox _minimizeToTrayCheckBox;
        private CheckBox _startWithWindowsCheckBox;
        private CheckBox _startMinimizedCheckBox;
        private CheckBox _trayNotificationsCheckBox;
        private CheckBox _detailsVisibleCheckBox;
        private CheckBox _automaticUpdatesCheckBox;

        private void CreateApplicationBehaviorControls()
        {
            FlowLayoutPanel panel = new FlowLayoutPanel
            {
                Name = "applicationBehaviorPanel",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = new Padding(0, 16, 0, 8),
                Padding = new Padding(0, 8, 0, 8),
                BackColor = Color.Transparent
            };
            Label heading = new Label { AutoSize = true, Text = "APPLICATION BEHAVIOR", Font = ThemeFonts.Eyebrow, ForeColor = Theme.AccentBright, Margin = new Padding(0, 0, 0, 8) };
            panel.Controls.Add(heading);
            _closeToTrayCheckBox = AddBehaviorCheckBox(panel, "Close window to tray", "Closing hides the window so uploads continue. Exit from the tray menu closes the app.");
            _minimizeToTrayCheckBox = AddBehaviorCheckBox(panel, "Minimize to tray", "Hide the taskbar window when minimized.");
            _startWithWindowsCheckBox = AddBehaviorCheckBox(panel, "Start with Windows", "Registers the installed executable for the current user with --background. No administrator rights are required.");
            _startMinimizedCheckBox = AddBehaviorCheckBox(panel, "Start minimized", "Start with only the tray icon when launched in the background.");
            _trayNotificationsCheckBox = AddBehaviorCheckBox(panel, "Show tray notifications", "Show aggregate completion and attention notifications while the window is hidden.");
            _detailsVisibleCheckBox = AddBehaviorCheckBox(panel, "Show Preview/Properties panel", "Show the file details panel when the Files page opens.");
            _automaticUpdatesCheckBox = AddBehaviorCheckBox(panel, "Check for updates automatically", "Check the configured HTTPS update channel at startup. An update is never installed without asking first.");

            int row = formLayout.RowCount;
            formLayout.RowCount++;
            formLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            formLayout.Controls.Add(panel, 0, row);
            formLayout.SetColumnSpan(panel, 2);
        }

        private CheckBox AddBehaviorCheckBox(FlowLayoutPanel panel, string text, string explanation)
        {
            CheckBox checkBox = new CheckBox { AutoSize = true, Text = text, ForeColor = Theme.TextSecondary, Font = ThemeFonts.Body, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 3, 0, 3), AccessibleDescription = explanation };
            panel.Controls.Add(checkBox);
            toolTip.SetToolTip(checkBox, explanation);
            return checkBox;
        }

        private void LoadApplicationBehaviorValues(AppSettings settings)
        {
            _closeToTrayCheckBox.Checked = settings.CloseToTray;
            _minimizeToTrayCheckBox.Checked = settings.MinimizeToTray;
            _startWithWindowsCheckBox.Checked = _startupRegistration.IsEnabled(Application.ExecutablePath);
            _startMinimizedCheckBox.Checked = settings.StartMinimized;
            _trayNotificationsCheckBox.Checked = settings.ShowTrayNotifications;
            _detailsVisibleCheckBox.Checked = settings.DetailsPanelVisible;
            _automaticUpdatesCheckBox.Checked = settings.CheckForUpdatesAutomatically;
        }

        private void ApplyApplicationBehaviorValues(AppSettings settings)
        {
            settings.CloseToTray = _closeToTrayCheckBox.Checked;
            settings.MinimizeToTray = _minimizeToTrayCheckBox.Checked;
            settings.StartWithWindows = _startWithWindowsCheckBox.Checked;
            settings.StartMinimized = _startMinimizedCheckBox.Checked;
            settings.ShowTrayNotifications = _trayNotificationsCheckBox.Checked;
            settings.DetailsPanelVisible = _detailsVisibleCheckBox.Checked;
            settings.CheckForUpdatesAutomatically = _automaticUpdatesCheckBox.Checked;
        }
    }
}
