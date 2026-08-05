using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using CloudflareR2Uploader.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CloudflareR2Uploader.Wpf.ViewModels.Dialogs
{
    /// <summary>
    /// Implemented by a dialog view model that decides when its window closes.
    /// <c>ThemedDialogWindow</c> translates the event into <c>Window.DialogResult</c>, so no
    /// view model ever touches a <c>Window</c>.
    /// </summary>
    public interface IDialogCloseRequester
    {
        /// <summary>True closes with a positive result, false cancels.</summary>
        event EventHandler<bool>? CloseRequested;
    }

    /// <summary>
    /// Backs every message, confirmation and two-choice dialog, matching the DIALOG ANATOMY
    /// block of Images/09-design-system-components.png.
    /// </summary>
    public sealed partial class MessageDialogViewModel : ObservableObject, IDialogCloseRequester
    {
        public MessageDialogViewModel(string title, string message)
        {
            Title = title;
            Message = message;
        }

        public string Title { get; }

        public string Message { get; }

        /// <summary>Optional expandable block. Already sanitised by the caller.</summary>
        public string? Details { get; init; }

        public string DetailsHeader { get; init; } = "Details";

        public string ConfirmLabel { get; init; } = "OK";

        /// <summary>Second affirmative choice, e.g. "Discard parts" beside "Keep parts".</summary>
        public string? AlternateLabel { get; init; }

        public bool ShowCancel { get; init; }

        public bool IsDestructive { get; init; }

        public bool IsError { get; init; }

        /// <summary>Optional opt-in, e.g. "Also delete matching entries from the activity log".</summary>
        public string? OptionLabel { get; init; }

        public bool HasDetails => !string.IsNullOrWhiteSpace(Details);

        public bool HasAlternate => !string.IsNullOrWhiteSpace(AlternateLabel);

        public bool HasOption => !string.IsNullOrWhiteSpace(OptionLabel);

        [ObservableProperty]
        private bool _isOptionChecked;

        [ObservableProperty]
        private bool _areDetailsExpanded;

        /// <summary>True when the user picked <see cref="AlternateLabel"/> rather than confirm.</summary>
        public bool ChoseAlternate { get; private set; }

        /// <summary>Set by the view when the dialog should close. True confirms, false cancels.</summary>
        public event EventHandler<bool>? CloseRequested;

        [RelayCommand]
        private void Confirm() => CloseRequested?.Invoke(this, true);

        [RelayCommand]
        private void Alternate()
        {
            ChoseAlternate = true;
            CloseRequested?.Invoke(this, true);
        }

        [RelayCommand]
        private void Cancel() => CloseRequested?.Invoke(this, false);

        [RelayCommand]
        private void ToggleDetails() => AreDetailsExpanded = !AreDetailsExpanded;
    }

    /// <summary>Describes a single-line text prompt.</summary>
    public sealed class TextPromptRequest
    {
        public required string Title { get; init; }

        public required string Label { get; init; }

        public string InitialValue { get; init; } = string.Empty;

        public string ConfirmLabel { get; init; } = "OK";

        public string? Hint { get; init; }

        public string Placeholder { get; init; } = string.Empty;

        /// <summary>Select this many leading characters when the field is focused, e.g. the stem of a filename.</summary>
        public int InitialSelectionLength { get; init; } = -1;

        /// <summary>Returns null when the value is acceptable, or a message to show inline.</summary>
        public Func<string, string?>? Validate { get; init; }

        public bool UseMonospace { get; init; }
    }

    public sealed partial class TextPromptViewModel : ObservableObject, IDialogCloseRequester
    {
        private readonly TextPromptRequest _request;

        public TextPromptViewModel(TextPromptRequest request)
        {
            _request = request;
            _value = request.InitialValue;
            Validate();
        }

        public string Title => _request.Title;

        public string Label => _request.Label;

        public string ConfirmLabel => _request.ConfirmLabel;

        public string? Hint => _request.Hint;

        public string Placeholder => _request.Placeholder;

        public bool UseMonospace => _request.UseMonospace;

        public int InitialSelectionLength => _request.InitialSelectionLength;

        public bool HasHint => !string.IsNullOrWhiteSpace(Hint);

        [ObservableProperty]
        private string _value;

        [ObservableProperty]
        private string? _error;

        public bool HasError => !string.IsNullOrEmpty(Error);

        public event EventHandler<bool>? CloseRequested;

        partial void OnValueChanged(string value) => Validate();

        partial void OnErrorChanged(string? value)
        {
            OnPropertyChanged(nameof(HasError));
            ConfirmCommand.NotifyCanExecuteChanged();
        }

        private void Validate() => Error = _request.Validate?.Invoke(Value ?? string.Empty);

        private bool CanConfirm() => !HasError;

        [RelayCommand(CanExecute = nameof(CanConfirm))]
        private void Confirm() => CloseRequested?.Invoke(this, true);

        [RelayCommand]
        private void Cancel() => CloseRequested?.Invoke(this, false);
    }

    /// <summary>
    /// Asks what to do about an existing object key, preserving the four choices the
    /// WinForms build offered: overwrite, skip, upload under a new name, or cancel the run.
    /// </summary>
    public sealed partial class OverwritePromptViewModel : ObservableObject, IDialogCloseRequester
    {
        public OverwritePromptViewModel(string objectKey, long existingLength, DateTime existingModifiedUtc, long localLength)
        {
            ObjectKey = objectKey;
            ExistingDescription = string.Format(
                CultureInfo.CurrentCulture,
                "{0}, modified {1}",
                FileSizeFormatter.Format(existingLength),
                existingModifiedUtc.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.CurrentCulture));
            LocalDescription = FileSizeFormatter.Format(localLength);
        }

        public string ObjectKey { get; }

        public string ExistingDescription { get; }

        public string LocalDescription { get; }

        /// <summary>Defaults to Skip so that closing the dialog can never destroy data.</summary>
        public OverwriteDecision Decision { get; private set; } = OverwriteDecision.Skip;

        [ObservableProperty]
        private bool _applyToAll;

        public event EventHandler<bool>? CloseRequested;

        [RelayCommand]
        private void Overwrite() => Choose(OverwriteDecision.Overwrite);

        [RelayCommand]
        private void Skip() => Choose(OverwriteDecision.Skip);

        [RelayCommand]
        private void Rename() => Choose(OverwriteDecision.Rename);

        [RelayCommand]
        private void CancelAll() => Choose(OverwriteDecision.CancelAll);

        private void Choose(OverwriteDecision decision)
        {
            Decision = decision;
            CloseRequested?.Invoke(this, true);
        }
    }

    /// <summary>
    /// Shows a generated temporary download link.
    /// <para>
    /// The URL is held only for the lifetime of this dialog. It is never written to settings,
    /// to the log or to the activity history, which records only that a link was created and
    /// for which object.
    /// </para>
    /// </summary>
    public sealed partial class TemporaryLinkDialogViewModel : ObservableObject, IDialogCloseRequester
    {
        private readonly IClipboardService _clipboard;
        private readonly IProcessLauncher _launcher;

        public TemporaryLinkDialogViewModel(
            string objectKey,
            string url,
            DateTime expiresLocal,
            IClipboardService clipboard,
            IProcessLauncher launcher)
        {
            ObjectKey = objectKey;
            Url = url;
            ExpiresLocal = expiresLocal;
            _clipboard = clipboard;
            _launcher = launcher;
        }

        public string ObjectKey { get; }

        public string Url { get; }

        public DateTime ExpiresLocal { get; }

        public string ExpiryDescription =>
            "Expires " + ExpiresLocal.ToString("d MMM yyyy, HH:mm", CultureInfo.CurrentCulture);

        [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "WPF bindings require an instance property.")]
        public string Warning =>
            "Anyone who has this link can download the object until it expires. It is not saved, logged or added to the activity history.";

        [ObservableProperty]
        private bool _wasCopied;

        public event EventHandler<bool>? CloseRequested;

        [RelayCommand]
        private void Copy()
        {
            _clipboard.SetText(Url);
            WasCopied = true;
        }

        [RelayCommand]
        private void Open()
        {
            try { _launcher.Open(Url); }
            catch (Exception) { /* The shell refused to open it; the copy action still works. */ }
        }

        [RelayCommand]
        private void Close() => CloseRequested?.Invoke(this, true);
    }
}
