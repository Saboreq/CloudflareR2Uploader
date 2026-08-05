using System;
using System.ComponentModel;
using System.Windows;
using CloudflareR2Uploader.Wpf.ViewModels;

namespace CloudflareR2Uploader.Wpf.Views.Dialogs
{
    public partial class SettingsWindow : ThemedDialogWindow
    {
        private SettingsViewModel? _viewModel;
        private bool _syncingPassword;

        public SettingsWindow()
        {
            InitializeComponent();
            DataContextChanged += OnSettingsDataContextChanged;
            Closed += OnSettingsClosed;
        }

        private void OnSettingsDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = e.NewValue as SettingsViewModel;
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
                SyncPasswordFromViewModel();
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SettingsViewModel.SecretAccessKey)) SyncPasswordFromViewModel();
        }

        private void SyncPasswordFromViewModel()
        {
            if (_viewModel is null || SecretPasswordBox.Password == _viewModel.SecretAccessKey) return;
            _syncingPassword = true;
            try { SecretPasswordBox.Password = _viewModel.SecretAccessKey; }
            finally { _syncingPassword = false; }
        }

        private void OnSecretPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (!_syncingPassword && _viewModel is not null)
                _viewModel.SecretAccessKey = SecretPasswordBox.Password;
        }

        private void OnSettingsClosed(object? sender, EventArgs e)
        {
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                _viewModel.Dispose();
            }
            _viewModel = null;
        }
    }
}
