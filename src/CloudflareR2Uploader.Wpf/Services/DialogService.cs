using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Wpf.ViewModels.Dialogs;
using CloudflareR2Uploader.Wpf.Views.Dialogs;
using Microsoft.Win32;

namespace CloudflareR2Uploader.Wpf.Services
{
    /// <summary>Result of a confirmation that can also carry a secondary opt-in.</summary>
    public readonly record struct ConfirmationResult(bool Confirmed, bool OptionChecked);

    /// <summary>
    /// Every modal interaction the application performs.
    /// <para>
    /// View models depend on this interface rather than on <c>Window</c> so they stay
    /// testable: a test supplies a stub that answers immediately instead of showing UI.
    /// </para>
    /// </summary>
    public interface IDialogService
    {
        /// <summary>Informational message with a single OK button.</summary>
        Task ShowMessageAsync(string title, string message, string? details = null);

        /// <summary>Error message with an expandable, already-sanitised technical block.</summary>
        Task ShowErrorAsync(string title, string message, string? technicalDetails = null);

        /// <summary>Yes/No confirmation. <paramref name="isDestructive"/> styles the primary button as danger.</summary>
        Task<bool> ConfirmAsync(string title, string message, string confirmLabel, bool isDestructive = false);

        /// <summary>Confirmation with an extra checkbox, e.g. "Also delete matching activity entries".</summary>
        Task<ConfirmationResult> ConfirmAsync(
            string title, string message, string confirmLabel, string optionLabel, bool isDestructive = false);

        /// <summary>Single-line text prompt used by Rename, New folder and Move to.</summary>
        Task<string?> PromptForTextAsync(TextPromptRequest request);

        /// <summary>Asks what to do about an object key that already exists.</summary>
        Task<OverwriteDecision> AskOverwriteAsync(string objectKey, long existingLength, DateTime existingModifiedUtc, long localLength);

        /// <summary>Asks whether a cancelled multipart upload should keep its uploaded parts.</summary>
        Task<bool?> AskKeepPartsOnCancelAsync(int activeMultipartCount);

        /// <summary>Shows a generated temporary link with copy and open actions.</summary>
        Task ShowTemporaryLinkAsync(TemporaryLinkDialogViewModel viewModel);

        /// <summary>Opens the modal Settings window. Returns true when the user saved.</summary>
        Task<bool> ShowSettingsAsync(string? initialSection = null);

        /// <summary>Native folder picker. Returns null when cancelled.</summary>
        string? BrowseForFolder(string title, string? initialDirectory = null);

        /// <summary>Native multi-select file picker. Returns an empty array when cancelled.</summary>
        IReadOnlyList<string> BrowseForFiles(string title);

        /// <summary>Native save picker. Returns null when cancelled.</summary>
        string? BrowseForSaveFile(string title, string suggestedFileName, string filter);
    }

    public sealed class DialogService : IDialogService
    {
        private readonly Func<Window?> _ownerAccessor;
        private readonly Func<SettingsWindow> _settingsWindowFactory;

        public DialogService(Func<Window?> ownerAccessor, Func<SettingsWindow> settingsWindowFactory)
        {
            _ownerAccessor = ownerAccessor;
            _settingsWindowFactory = settingsWindowFactory;
        }

        public Task ShowMessageAsync(string title, string message, string? details = null)
        {
            MessageDialogViewModel viewModel = new(title, message)
            {
                Details = details,
                ConfirmLabel = "OK",
                ShowCancel = false
            };

            ShowModal(new MessageDialog { DataContext = viewModel });
            return Task.CompletedTask;
        }

        public Task ShowErrorAsync(string title, string message, string? technicalDetails = null)
        {
            MessageDialogViewModel viewModel = new(title, message)
            {
                Details = technicalDetails,
                DetailsHeader = "Technical details",
                ConfirmLabel = "Close",
                ShowCancel = false,
                IsError = true
            };

            ShowModal(new MessageDialog { DataContext = viewModel });
            return Task.CompletedTask;
        }

        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel, bool isDestructive = false)
        {
            MessageDialogViewModel viewModel = new(title, message)
            {
                ConfirmLabel = confirmLabel,
                ShowCancel = true,
                IsDestructive = isDestructive
            };

            bool? result = ShowModal(new MessageDialog { DataContext = viewModel });
            return Task.FromResult(result == true);
        }

        public Task<ConfirmationResult> ConfirmAsync(
            string title, string message, string confirmLabel, string optionLabel, bool isDestructive = false)
        {
            MessageDialogViewModel viewModel = new(title, message)
            {
                ConfirmLabel = confirmLabel,
                ShowCancel = true,
                IsDestructive = isDestructive,
                OptionLabel = optionLabel
            };

            bool? result = ShowModal(new MessageDialog { DataContext = viewModel });
            return Task.FromResult(new ConfirmationResult(result == true, viewModel.IsOptionChecked));
        }

        public Task<string?> PromptForTextAsync(TextPromptRequest request)
        {
            TextPromptViewModel viewModel = new(request);
            bool? result = ShowModal(new TextPromptDialog { DataContext = viewModel });
            return Task.FromResult(result == true ? viewModel.Value : null);
        }

        public Task<OverwriteDecision> AskOverwriteAsync(
            string objectKey, long existingLength, DateTime existingModifiedUtc, long localLength)
        {
            OverwritePromptViewModel viewModel = new(objectKey, existingLength, existingModifiedUtc, localLength);
            ShowModal(new OverwritePromptDialog { DataContext = viewModel });
            return Task.FromResult(viewModel.Decision);
        }

        public Task<bool?> AskKeepPartsOnCancelAsync(int activeMultipartCount)
        {
            MessageDialogViewModel viewModel = new(
                "Cancel the upload?",
                activeMultipartCount == 1
                    ? "One multipart upload is in progress. Its confirmed parts can be kept so it resumes where it stopped, or removed from the bucket now."
                    : activeMultipartCount + " multipart uploads are in progress. Their confirmed parts can be kept so they resume where they stopped, or removed from the bucket now.")
            {
                ConfirmLabel = "Keep parts for resume",
                AlternateLabel = "Discard parts",
                ShowCancel = true
            };

            bool? result = ShowModal(new MessageDialog { DataContext = viewModel });
            if (result != true) return Task.FromResult<bool?>(null);
            return Task.FromResult<bool?>(!viewModel.ChoseAlternate);
        }

        public Task ShowTemporaryLinkAsync(TemporaryLinkDialogViewModel viewModel)
        {
            ShowModal(new TemporaryLinkDialog { DataContext = viewModel });
            return Task.CompletedTask;
        }

        public Task<bool> ShowSettingsAsync(string? initialSection = null)
        {
            SettingsWindow window = _settingsWindowFactory();
            if (window.DataContext is ViewModels.SettingsViewModel viewModel && initialSection is not null)
            {
                viewModel.SelectSection(initialSection);
                if (string.Equals(initialSection, "updates", StringComparison.OrdinalIgnoreCase))
                {
                    window.ContentRendered += async (_, _) =>
                    {
                        if (viewModel.CheckForUpdatesCommand.IsRunning) return;
                        await viewModel.CheckForUpdatesCommand.ExecuteAsync(null).ConfigureAwait(true);
                    };
                }
            }

            bool? result = ShowModal(window);
            return Task.FromResult(result == true);
        }

        public string? BrowseForFolder(string title, string? initialDirectory = null)
        {
            // .NET 10 exposes the real IFileDialog-based folder picker, so no third-party
            // dialog package is needed any more.
            OpenFolderDialog dialog = new()
            {
                Title = title,
                Multiselect = false
            };

            if (!string.IsNullOrEmpty(initialDirectory)) dialog.InitialDirectory = initialDirectory;

            return dialog.ShowDialog(_ownerAccessor()) == true ? dialog.FolderName : null;
        }

        public IReadOnlyList<string> BrowseForFiles(string title)
        {
            OpenFileDialog dialog = new()
            {
                Title = title,
                Multiselect = true,
                CheckFileExists = true,
                Filter = "All files (*.*)|*.*"
            };

            return dialog.ShowDialog(_ownerAccessor()) == true ? dialog.FileNames.ToArray() : Array.Empty<string>();
        }

        public string? BrowseForSaveFile(string title, string suggestedFileName, string filter)
        {
            SaveFileDialog dialog = new()
            {
                Title = title,
                FileName = suggestedFileName,
                Filter = filter,
                OverwritePrompt = true,
                AddExtension = true
            };

            return dialog.ShowDialog(_ownerAccessor()) == true ? dialog.FileName : null;
        }

        private bool? ShowModal(Window window)
        {
            Window? owner = _ownerAccessor();

            // An owner makes the dialog centre on the app, stay above it, and restore focus to
            // the invoking control when it closes.
            if (owner is not null && !ReferenceEquals(owner, window) && owner.IsLoaded) window.Owner = owner;

            return window.ShowDialog();
        }
    }
}
