using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CloudflareR2Uploader.Wpf.Services
{
    public enum ToastKind
    {
        Success = 0,
        Error = 1,
        Progress = 2
    }

    /// <summary>
    /// One in-application notification, as shown in the TOASTS block of
    /// Images/09-design-system-components.png.
    /// </summary>
    public sealed partial class ToastViewModel : ObservableObject
    {
        private readonly Action<ToastViewModel> _dismiss;

        public ToastViewModel(ToastKind kind, string message, Action<ToastViewModel> dismiss,
            string? actionLabel = null, Action? action = null)
        {
            Kind = kind;
            Message = message;
            ActionLabel = actionLabel;
            _dismiss = dismiss;
            _action = action;
        }

        private readonly Action? _action;

        public ToastKind Kind { get; }

        public string Message { get; }

        /// <summary>Optional trailing action, e.g. "Retry" on a failed upload toast.</summary>
        public string? ActionLabel { get; }

        public bool HasAction => !string.IsNullOrEmpty(ActionLabel) && _action is not null;

        [ObservableProperty]
        private double _progressPercent;

        [ObservableProperty]
        private bool _showsProgress;

        [RelayCommand]
        private void Dismiss() => _dismiss(this);

        [RelayCommand]
        private void Invoke()
        {
            _action?.Invoke();
            _dismiss(this);
        }
    }

    /// <summary>
    /// Shows transient in-application notifications. Distinct from the tray balloon, which is
    /// for things the user needs to see while the window is hidden.
    /// </summary>
    public interface IToastService
    {
        ReadOnlyObservableCollection<ToastViewModel> Toasts { get; }

        void ShowSuccess(string message);

        void ShowError(string message, string? actionLabel = null, Action? action = null);

        /// <summary>A toast that stays until it is dismissed or replaced. Returns it so the caller can update progress.</summary>
        ToastViewModel ShowProgress(string message);

        void Dismiss(ToastViewModel toast);
    }

    public sealed class ToastService : IToastService
    {
        /// <summary>Long enough to read a sentence, short enough not to obscure the status bar.</summary>
        private static readonly TimeSpan SuccessLifetime = TimeSpan.FromSeconds(4);

        private static readonly TimeSpan ErrorLifetime = TimeSpan.FromSeconds(9);

        /// <summary>More than this on screen at once is noise, not information.</summary>
        private const int MaxVisible = 4;

        private readonly ObservableCollection<ToastViewModel> _toasts = new();
        private readonly Dispatcher _dispatcher;

        public ToastService()
        {
            _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            Toasts = new ReadOnlyObservableCollection<ToastViewModel>(_toasts);
        }

        public ReadOnlyObservableCollection<ToastViewModel> Toasts { get; }

        public void ShowSuccess(string message) => Add(new ToastViewModel(ToastKind.Success, message, Dismiss), SuccessLifetime);

        public void ShowError(string message, string? actionLabel = null, Action? action = null) =>
            Add(new ToastViewModel(ToastKind.Error, message, Dismiss, actionLabel, action), ErrorLifetime);

        public ToastViewModel ShowProgress(string message)
        {
            ToastViewModel toast = new(ToastKind.Progress, message, Dismiss) { ShowsProgress = true };
            Add(toast, lifetime: null);
            return toast;
        }

        public void Dismiss(ToastViewModel toast)
        {
            if (_dispatcher.CheckAccess()) _toasts.Remove(toast);
            else _dispatcher.BeginInvoke(new Action(() => _toasts.Remove(toast)));
        }

        private void Add(ToastViewModel toast, TimeSpan? lifetime)
        {
            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke(new Action(() => Add(toast, lifetime)));
                return;
            }

            _toasts.Add(toast);
            while (_toasts.Count > MaxVisible) _toasts.RemoveAt(0);

            if (lifetime is null) return;

            // One timer per toast is acceptable: at most MaxVisible exist, and each stops
            // itself on its first tick.
            DispatcherTimer timer = new(DispatcherPriority.Normal, _dispatcher) { Interval = lifetime.Value };
            timer.Tick += (sender, args) =>
            {
                timer.Stop();
                _toasts.Remove(toast);
            };
            timer.Start();
        }
    }
}
