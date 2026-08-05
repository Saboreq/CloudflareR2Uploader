using System;
using System.Windows;
using CloudflareR2Uploader.Wpf.ViewModels.Dialogs;

namespace CloudflareR2Uploader.Wpf.Views.Dialogs
{
    public partial class TextPromptDialog : ThemedDialogWindow
    {
        public TextPromptDialog()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        /// <summary>
        /// Focus and initial selection are genuine view responsibilities: a rename dialog has
        /// to open with the filename stem selected so typing replaces the name but keeps the
        /// extension, and that cannot be expressed as a binding.
        /// </summary>
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ValueBox.Focus();

            if (DataContext is not TextPromptViewModel viewModel) return;

            int length = viewModel.InitialSelectionLength;
            if (length < 0 || length > ValueBox.Text.Length) ValueBox.SelectAll();
            else ValueBox.Select(0, length);
        }
    }
}
