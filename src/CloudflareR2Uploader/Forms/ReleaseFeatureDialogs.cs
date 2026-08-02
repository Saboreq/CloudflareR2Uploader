using System;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using CloudflareR2Uploader.Theming;

namespace CloudflareR2Uploader.Forms
{
    internal sealed class BulkConflictDialog : Form
    {
        private readonly CheckBox _remaining;
        public BulkConflictDialog(string path)
        {
            Text = "Download conflict"; Size = new Size(560, 260); MinimumSize = Size; MaximumSize = Size; StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.WindowBackground; ForeColor = Theme.TextPrimary; Font = ThemeFonts.Body; Icon = Program.LoadApplicationIcon();
            Label text = new Label { Dock = DockStyle.Top, Height = 100, Padding = new Padding(20), Text = "A file already exists:\r\n\r\n" + (path ?? string.Empty).Replace("\r", " ").Replace("\n", " "), ForeColor = Theme.TextPrimary };
            _remaining = new CheckBox { Dock = DockStyle.Top, Height = 34, Padding = new Padding(20, 0, 0, 0), Text = "Use this choice for the remaining conflicts", ForeColor = Theme.TextSecondary };
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 56, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
            Add(buttons, "Skip", BulkConflictBehavior.Skip); Add(buttons, "Rename", BulkConflictBehavior.RenameAutomatically); Add(buttons, "Overwrite", BulkConflictBehavior.Overwrite);
            Controls.Add(text); Controls.Add(_remaining); Controls.Add(buttons);
        }
        public BulkConflictDecision Decision { get; private set; }
        private void Add(FlowLayoutPanel panel, string text, BulkConflictBehavior behavior)
        {
            Button button = new Button { Text = text, Width = 105, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = Theme.ElevatedBackground, ForeColor = Theme.TextPrimary };
            button.Click += (s, e) => { Decision = new BulkConflictDecision { Behavior = behavior, ApplyToRemaining = _remaining.Checked }; DialogResult = DialogResult.OK; Close(); };
            panel.Controls.Add(button);
        }
    }

    internal sealed class BulkOperationProgressDialog : Form
    {
        private readonly Label _state;
        private readonly Label _detail;
        private readonly ProgressBar _progress;
        public BulkOperationProgressDialog(string title, CancellationTokenSource cancellation)
        {
            Text = title; Size = new Size(560, 245); StartPosition = FormStartPosition.CenterParent; ControlBox = false; BackColor = Theme.WindowBackground; ForeColor = Theme.TextPrimary; Font = ThemeFonts.Body; Icon = Program.LoadApplicationIcon();
            _state = new Label { Dock = DockStyle.Top, Height = 52, Padding = new Padding(20, 18, 20, 0), Font = ThemeFonts.BodyBold, ForeColor = Theme.TextPrimary };
            _detail = new Label { Dock = DockStyle.Top, Height = 70, Padding = new Padding(20, 8, 20, 0), ForeColor = Theme.TextSecondary, AutoEllipsis = true };
            _progress = new ProgressBar { Dock = DockStyle.Top, Height = 20, Style = ProgressBarStyle.Marquee };
            Button cancel = new Button { Dock = DockStyle.Bottom, Height = 40, Text = "Cancel", FlatStyle = FlatStyle.Flat, ForeColor = Theme.TextPrimary, BackColor = Theme.ElevatedBackground };
            cancel.Click += (s, e) => { cancel.Enabled = false; cancel.Text = "Cancelling..."; cancellation.Cancel(); };
            Controls.Add(cancel); Controls.Add(_progress); Controls.Add(_detail); Controls.Add(_state);
        }
        public void Report(BulkOperationProgress value)
        {
            if (value == null) return;
            _state.Text = value.State ?? "Working...";
            _detail.Text = (value.CurrentObject ?? string.Empty) + "\r\n" + value.CompletedObjects + " / " + value.TotalObjects + " objects · " + Utilities.FileSizeFormatter.Format(value.TransferredBytes) + " / " + Utilities.FileSizeFormatter.Format(value.TotalBytes);
            if (value.TotalObjects > 0) { _progress.Style = ProgressBarStyle.Continuous; _progress.Value = Math.Max(0, Math.Min(100, (int)((long)value.CompletedObjects * 100 / value.TotalObjects))); }
        }
    }

    internal sealed class PresignedUrlDialog : Form
    {
        private readonly R2PresignedUrlService _service;
        private readonly AppSettings _settings;
        private readonly R2Credentials _credentials;
        private readonly R2BrowserItem _item;
        private readonly ComboBox _expiration;
        private readonly NumericUpDown _customMinutes;
        private readonly TextBox _url;
        private readonly Label _expires;
        private readonly IClipboardService _clipboard;
        private readonly IProcessLauncher _launcher;

        public PresignedUrlDialog(AppSettings settings, R2Credentials credentials, R2BrowserItem item, IClipboardService clipboard, IProcessLauncher launcher)
        {
            _service = new R2PresignedUrlService(); _settings = settings; _credentials = credentials; _item = item; _clipboard = clipboard; _launcher = launcher;
            Text = "Temporary download link"; Size = new Size(700, 430); MinimumSize = new Size(620, 390); StartPosition = FormStartPosition.CenterParent; BackColor = Theme.WindowBackground; ForeColor = Theme.TextPrimary; Font = ThemeFonts.Body; Icon = Program.LoadApplicationIcon();
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 8 }; layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Add(layout, "File", new Label { Text = item.DisplayName, AutoEllipsis = true, Dock = DockStyle.Fill }); Add(layout, "Object key", new TextBox { Text = item.Key, ReadOnly = true, Dock = DockStyle.Fill, BackColor = Theme.InputBackground, ForeColor = Theme.TextPrimary });
            _expiration = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Left, Width = 180 }; _expiration.Items.AddRange(new object[] { "15 minutes", "1 hour", "6 hours", "1 day", "7 days", "Custom" }); _expiration.SelectedIndex = 1; Add(layout, "Expiration", _expiration);
            _customMinutes = new NumericUpDown { Minimum = 1, Maximum = 10080, Value = 60, Dock = DockStyle.Left, Width = 180, Enabled = false }; Add(layout, "Custom minutes", _customMinutes);
            _url = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, Height = 90, BackColor = Theme.InputBackground, ForeColor = Theme.TextPrimary, ScrollBars = ScrollBars.Vertical }; Add(layout, "Generated URL", _url);
            _expires = new Label { Dock = DockStyle.Fill, AutoSize = true }; Add(layout, "Expires", _expires);
            Label warning = new Label { Text = "Anyone with this bearer URL can download the object until it expires. Do not post it publicly.", ForeColor = Theme.Warning, Dock = DockStyle.Fill, AutoSize = true }; layout.Controls.Add(warning, 0, 6); layout.SetColumnSpan(warning, 2);
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft }; Button close = AddButton(buttons, "Close", (s, e) => Close()); Button open = AddButton(buttons, "Open", OnOpen); Button copy = AddButton(buttons, "Copy link", OnCopy); Button regenerate = AddButton(buttons, "Regenerate", (s, e) => Generate()); layout.Controls.Add(buttons, 0, 7); layout.SetColumnSpan(buttons, 2);
            Controls.Add(layout); _expiration.SelectedIndexChanged += (s, e) => { _customMinutes.Enabled = _expiration.SelectedIndex == 5; Generate(); }; _customMinutes.ValueChanged += (s, e) => { if (_customMinutes.Enabled) Generate(); }; Shown += (s, e) => Generate();
        }
        private static void Add(TableLayoutPanel layout, string name, Control control) { int row = layout.GetRowHeights().Length == 0 ? 0 : layout.Controls.Count / 2; Label label = new Label { Text = name, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.TextSecondary }; layout.Controls.Add(label, 0, row); layout.Controls.Add(control, 1, row); }
        private static Button AddButton(FlowLayoutPanel panel, string text, EventHandler handler) { Button button = new Button { Text = text, AutoSize = true, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = Theme.ElevatedBackground, ForeColor = Theme.TextPrimary }; button.Click += handler; panel.Controls.Add(button); return button; }
        private TimeSpan GetExpiration() { switch (_expiration.SelectedIndex) { case 0: return TimeSpan.FromMinutes(15); case 1: return TimeSpan.FromHours(1); case 2: return TimeSpan.FromHours(6); case 3: return TimeSpan.FromDays(1); case 4: return TimeSpan.FromDays(7); default: return TimeSpan.FromMinutes((double)_customMinutes.Value); } }
        private void Generate() { try { PresignedUrlResult result = _service.Create(_settings, _credentials, _item.Key, GetExpiration()); _url.Text = result.Url; _expires.Text = result.ExpiresLocal.ToString("F", CultureInfo.CurrentCulture); } catch (Exception ex) { _url.Clear(); _expires.Text = LoggingService.Sanitize(ex.Message); } }
        private void OnCopy(object sender, EventArgs e) { try { _clipboard.SetText(_url.Text); } catch (Exception ex) { MessageBox.Show(this, LoggingService.Sanitize(ex.Message), "Clipboard unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
        private void OnOpen(object sender, EventArgs e) { try { _launcher.Open(_url.Text); } catch (Exception ex) { MessageBox.Show(this, LoggingService.Sanitize(ex.Message), "Could not open link", MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
    }

    internal sealed class BulkOperationSummaryDialog : Form
    {
        public BulkOperationSummaryDialog(string title, string details, string destination, bool hasFailures)
        {
            Text = title; Size = new Size(680, 470); MinimumSize = new Size(560, 380); StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.WindowBackground; ForeColor = Theme.TextPrimary; Font = ThemeFonts.Body; Icon = Program.LoadApplicationIcon();
            Label headline = new Label { Dock = DockStyle.Top, Height = 58, Padding = new Padding(18), Text = title, Font = ThemeFonts.Title, ForeColor = hasFailures ? Theme.Warning : Theme.Success };
            TextBox text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Text = details, BackColor = Theme.InputBackground, ForeColor = Theme.TextPrimary, Font = ThemeFonts.Mono };
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
            AddSummaryButton(buttons, "Close", (s, e) => Close());
            AddSummaryButton(buttons, "Copy details", (s, e) => { try { Clipboard.SetText(details ?? string.Empty); } catch (Exception ex) { MessageBox.Show(this, LoggingService.Sanitize(ex.Message), "Clipboard unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning); } });
            if (!string.IsNullOrEmpty(destination)) AddSummaryButton(buttons, "Open destination", (s, e) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(destination) { UseShellExecute = true }); } catch (Exception ex) { MessageBox.Show(this, LoggingService.Sanitize(ex.Message), "Could not open destination", MessageBoxButtons.OK, MessageBoxIcon.Warning); } });
            Controls.Add(text); Controls.Add(buttons); Controls.Add(headline);
        }
        private static void AddSummaryButton(FlowLayoutPanel panel, string text, EventHandler handler) { Button button = new Button { Text = text, AutoSize = true, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = Theme.ElevatedBackground, ForeColor = Theme.TextPrimary }; button.Click += handler; panel.Controls.Add(button); }
    }
}
