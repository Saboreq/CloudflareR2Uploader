using System;
using System.Drawing;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Theming;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CloudflareR2Uploader.Controls
{
    public sealed class R2DetailsPanel : UserControl
    {
        private readonly TabControl _tabs;
        private readonly R2PreviewPanel _preview;
        private readonly R2PropertiesPanel _properties;

        public R2DetailsPanel()
        {
            BackColor = Theme.CardBackground;
            _tabs = new TabControl { Dock = DockStyle.Fill, AccessibleName = "Object details" };
            TabPage previewTab = new TabPage("Preview") { BackColor = Theme.CardBackground };
            TabPage propertiesTab = new TabPage("Properties") { BackColor = Theme.CardBackground };
            _preview = new R2PreviewPanel { Dock = DockStyle.Fill };
            _properties = new R2PropertiesPanel { Dock = DockStyle.Fill };
            previewTab.Controls.Add(_preview);
            propertiesTab.Controls.Add(_properties);
            _tabs.TabPages.Add(previewTab);
            _tabs.TabPages.Add(propertiesTab);
            Controls.Add(_tabs);
        }

        public event EventHandler CopyKeyRequested { add { _properties.CopyKeyRequested += value; } remove { _properties.CopyKeyRequested -= value; } }
        public event EventHandler CopyPublicUrlRequested { add { _properties.CopyPublicUrlRequested += value; } remove { _properties.CopyPublicUrlRequested -= value; } }
        public event EventHandler OpenPublicUrlRequested { add { _properties.OpenPublicUrlRequested += value; } remove { _properties.OpenPublicUrlRequested -= value; } }
        public event EventHandler TemporaryLinkRequested { add { _properties.TemporaryLinkRequested += value; } remove { _properties.TemporaryLinkRequested -= value; } }
        public int SelectedTab { get { return _tabs.SelectedIndex; } set { _tabs.SelectedIndex = value < 0 || value > 1 ? 0 : value; } }
        public event EventHandler SelectedTabChanged { add { _tabs.SelectedIndexChanged += value; } remove { _tabs.SelectedIndexChanged -= value; } }

        public void ShowNeutral(string text) { _preview.ShowMessage(text); _properties.ShowText(text); }
        public void ShowLoading() { _preview.ShowMessage("Loading preview..."); _properties.ShowText("Loading object properties..."); }
        public void ShowAggregate(BrowserSelectionSummary summary)
        {
            string text = summary.SelectedCount + " selected\r\n\r\n" + summary.FolderCount + " folder(s)\r\n" + summary.FileCount + " file(s)\r\n" +
                          Utilities.FileSizeFormatter.Format(summary.ListedFileSize) + " directly listed file size\r\n" +
                          (summary.EarliestModifiedUtc.HasValue ? "Earliest listed modification: " + summary.EarliestModifiedUtc.Value.ToLocalTime().ToString("G") + "\r\n" : string.Empty) +
                          (summary.LatestModifiedUtc.HasValue ? "Latest listed modification: " + summary.LatestModifiedUtc.Value.ToLocalTime().ToString("G") + "\r\n" : string.Empty) +
                          "\r\nFolder contents are not included until an operation performs preflight enumeration.";
            _preview.ShowMessage("Multiple objects selected\r\n\r\n" + text);
            _properties.ShowText(text);
            _properties.SetLinkActions(false, false);
        }
        public void ShowProperties(R2ObjectProperties value)
        {
            StringBuilder text = new StringBuilder();
            Add(text, "Name", value.Name); Add(text, value.IsFolder ? "Prefix" : "Object key", value.KeyOrPrefix);
            Add(text, "Bucket", value.Bucket); Add(text, "Profile", value.ProfileName); Add(text, "Endpoint", value.EndpointHost); Add(text, "Type", value.Type);
            if (value.IsFolder) text.AppendLine().AppendLine("R2 folders are virtual key prefixes. Size, modified time, and object count require a separate recursive scan.");
            else
            {
                Add(text, "Extension", value.Extension); Add(text, "Size", value.Size.HasValue ? Utilities.FileSizeFormatter.Format(value.Size.Value) : string.Empty);
                Add(text, "Last modified (local)", value.LastModifiedUtc.HasValue ? value.LastModifiedUtc.Value.ToLocalTime().ToString("G") : string.Empty);
                Add(text, "Last modified (UTC)", value.LastModifiedUtc.HasValue ? value.LastModifiedUtc.Value.ToString("u") : string.Empty);
                Add(text, "ETag", value.ETag); Add(text, "Content-Type", value.ContentType); Add(text, "Cache-Control", value.CacheControl);
                Add(text, "Content-Disposition", value.ContentDisposition); Add(text, "Content-Encoding", value.ContentEncoding); Add(text, "Content-Language", value.ContentLanguage);
                Add(text, "Expires (UTC)", value.ExpiresUtc.HasValue ? value.ExpiresUtc.Value.ToString("u") : string.Empty);
                Add(text, "Version ID", value.VersionId); Add(text, "Storage class", value.StorageClass); Add(text, "Checksums", value.Checksum);
                Add(text, "Public URL", value.PublicUrl);
                if (value.CustomMetadata.Count > 0) { text.AppendLine().AppendLine("Custom metadata"); foreach (var pair in value.CustomMetadata) Add(text, pair.Key, pair.Value); }
                if (value.ExtractedMetadata.Count > 0) { text.AppendLine().AppendLine("Extracted preview metadata"); foreach (var pair in value.ExtractedMetadata) Add(text, pair.Key, pair.Value); }
            }
            _properties.ShowText(text.ToString());
            _properties.SetLinkActions(!value.IsFolder, !value.IsFolder && !string.IsNullOrEmpty(value.PublicUrl));
        }
        public Task ShowPreviewAsync(R2PreviewResult value, string webViewDataDirectory) { return _preview.ShowPreviewAsync(value, webViewDataDirectory); }
        public void DisposePreview() { _preview.DisposePreview(); }
        private static void Add(StringBuilder text, string name, string value) { if (!string.IsNullOrWhiteSpace(value)) text.Append(name).Append(": ").AppendLine(value.Replace("\r", " ").Replace("\n", " ")); }
    }

    public sealed class R2PropertiesPanel : UserControl
    {
        private readonly RichTextBox _text;
        private readonly FlowLayoutPanel _buttons;
        private readonly Button _copyKey;
        private readonly Button _copyPublic;
        private readonly Button _openPublic;
        private readonly Button _temporary;
        public R2PropertiesPanel()
        {
            BackColor = Theme.CardBackground;
            _buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 82, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, BackColor = Color.Transparent };
            _copyKey = Add("Copy key", (s, e) => Raise(CopyKeyRequested));
            _copyPublic = Add("Copy public URL", (s, e) => Raise(CopyPublicUrlRequested));
            _openPublic = Add("Open public URL", (s, e) => Raise(OpenPublicUrlRequested));
            _temporary = Add("Temporary link", (s, e) => Raise(TemporaryLinkRequested));
            _text = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.CardBackground, ForeColor = Theme.TextPrimary, Font = ThemeFonts.Body, DetectUrls = false, AccessibleName = "Object properties" };
            Controls.Add(_text); Controls.Add(_buttons);
        }
        public event EventHandler CopyKeyRequested;
        public event EventHandler CopyPublicUrlRequested;
        public event EventHandler OpenPublicUrlRequested;
        public event EventHandler TemporaryLinkRequested;
        private Button Add(string text, EventHandler handler) { Button button = new Button { AutoSize = true, Text = text, FlatStyle = FlatStyle.Flat, ForeColor = Theme.TextPrimary, BackColor = Theme.ElevatedBackground }; button.Click += handler; _buttons.Controls.Add(button); return button; }
        private void Raise(EventHandler handler) { if (handler != null) handler(this, EventArgs.Empty); }
        public void ShowText(string text) { _text.Text = text ?? string.Empty; }
        public void SetLinkActions(bool file, bool publicUrl) { _copyKey.Enabled = file; _copyPublic.Enabled = publicUrl; _openPublic.Enabled = publicUrl; _temporary.Enabled = file; }
    }

    public sealed class R2PreviewPanel : UserControl
    {
        private readonly TextBox _text;
        private readonly PictureBox _image;
        private readonly Label _message;
        private readonly Button _runtimeButton;
        private WebView2 _webView;
        private bool _webViewConfigured;
        public R2PreviewPanel()
        {
            BackColor = Theme.CardBackground;
            _text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = ThemeFonts.Mono, BackColor = Theme.InputBackground, ForeColor = Theme.TextPrimary, BorderStyle = BorderStyle.None };
            _image = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.CardBackground };
            _message = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.TextMuted, Font = ThemeFonts.Body, Padding = new Padding(20) };
            _runtimeButton = new Button { Dock = DockStyle.Bottom, Height = 38, Text = "Get WebView2 Runtime", Visible = false, FlatStyle = FlatStyle.Flat, BackColor = Theme.ElevatedBackground, ForeColor = Theme.TextPrimary };
            _runtimeButton.Click += (s, e) => { try { Process.Start(new ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/") { UseShellExecute = true }); } catch (Exception ex) { ShowMessage("Open https://developer.microsoft.com/microsoft-edge/webview2/ in a browser.\r\n\r\n" + ex.Message); } };
            Controls.Add(_text); Controls.Add(_image); Controls.Add(_message); Controls.Add(_runtimeButton);
            ShowMessage("Select a file to preview it.");
        }
        public void ShowMessage(string text) { HideAll(); _message.Text = text; _message.Visible = true; }
        public async Task ShowPreviewAsync(R2PreviewResult value, string webViewDataDirectory)
        {
            if (value == null) { ShowMessage("No preview is available."); return; }
            if (!string.IsNullOrEmpty(value.Warning) && string.IsNullOrEmpty(value.Text) && string.IsNullOrEmpty(value.LocalPath)) { ShowMessage(value.Warning); return; }
            if (value.Kind == R2PreviewKind.Image && File.Exists(value.LocalPath))
            {
                try { HideAll(); using (Image source = Image.FromFile(value.LocalPath)) _image.Image = new Bitmap(source); _image.Visible = true; return; }
                catch (ArgumentException) { await ShowLocalWebViewAsync(value.LocalPath, webViewDataDirectory, "This image format needs Microsoft Edge WebView2 Evergreen Runtime for preview."); return; }
                catch (OutOfMemoryException) { await ShowLocalWebViewAsync(value.LocalPath, webViewDataDirectory, "This image format needs Microsoft Edge WebView2 Evergreen Runtime for preview."); return; }
            }
            if (value.Kind == R2PreviewKind.Pdf && File.Exists(value.LocalPath))
            {
                await ShowLocalWebViewAsync(value.LocalPath, webViewDataDirectory, "PDF preview needs Microsoft Edge WebView2 Evergreen Runtime. The rest of the application remains available."); return;
            }
            HideAll(); _text.Text = (value.Text ?? string.Empty) + (value.Truncated ? "\r\n\r\n[Preview truncated]" : string.Empty) + (!string.IsNullOrEmpty(value.Warning) ? "\r\n\r\n[" + value.Warning + "]" : string.Empty); _text.Visible = true;
        }
        private void HideAll() { _text.Visible = false; _message.Visible = false; _runtimeButton.Visible = false; if (_image.Image != null) { _image.Image.Dispose(); _image.Image = null; } _image.Visible = false; if (_webView != null) _webView.Visible = false; }
        private async Task ShowLocalWebViewAsync(string localPath, string webViewDataDirectory, string unavailableMessage)
        {
            try
            {
                if (_webView == null) { _webView = new WebView2 { Dock = DockStyle.Fill }; Controls.Add(_webView); _webView.BringToFront(); }
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, webViewDataDirectory);
                await _webView.EnsureCoreWebView2Async(environment);
                if (!_webViewConfigured)
                {
                    _webView.CoreWebView2.Settings.AreDevToolsEnabled = false; _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false; _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                    _webView.CoreWebView2.NewWindowRequested += (s, e) => e.Handled = true;
                    _webView.CoreWebView2.NavigationStarting += (s, e) => { Uri uri; if (Uri.TryCreate(e.Uri, UriKind.Absolute, out uri) && (uri.Scheme == "http" || uri.Scheme == "https")) e.Cancel = true; };
                    _webViewConfigured = true;
                }
                HideAll(); _webView.Visible = true; _webView.Source = new Uri(localPath);
            }
            catch (Exception) { ShowMessage(unavailableMessage + "\r\n\r\nInstall the Evergreen Runtime from Microsoft, then select the object again."); _runtimeButton.Visible = true; _runtimeButton.BringToFront(); }
        }
        public void DisposePreview() { HideAll(); if (_webView != null) { _webView.Dispose(); _webView = null; } }
        protected override void Dispose(bool disposing) { if (disposing) DisposePreview(); base.Dispose(disposing); }
    }
}
