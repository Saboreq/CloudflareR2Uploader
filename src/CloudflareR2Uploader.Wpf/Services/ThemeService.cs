using System;
using System.Windows;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;

namespace CloudflareR2Uploader.Wpf.Services
{
    /// <summary>
    /// Applies the selected palette by swapping one merged dictionary.
    /// <para>
    /// Every brush is consumed through <c>DynamicResource</c>, so replacing the dictionary at
    /// index 0 of <see cref="Application.Resources"/> repaints the whole application without
    /// rebuilding a single view or re-running a single template.
    /// </para>
    /// </summary>
    public interface IThemeService
    {
        AppTheme Theme { get; }

        /// <summary>True when the palette currently in force is the dark one.</summary>
        bool IsDark { get; }

        event EventHandler? ThemeChanged;

        void Apply(AppTheme theme);
    }

    public sealed class ThemeService : IThemeService, IDisposable
    {
        private static readonly Uri DarkPalette = new("Themes/Theme.Dark.xaml", UriKind.Relative);
        private static readonly Uri LightPalette = new("Themes/Theme.Light.xaml", UriKind.Relative);

        /// <summary>The palette always occupies this slot; App.xaml documents the contract.</summary>
        private const int PaletteDictionaryIndex = 0;

        private readonly ISystemThemeProvider _systemTheme;
        private bool _disposed;

        public ThemeService(ISystemThemeProvider systemTheme)
        {
            _systemTheme = systemTheme ?? throw new ArgumentNullException(nameof(systemTheme));
            _systemTheme.Changed += OnSystemThemeChanged;
        }

        public AppTheme Theme { get; private set; } = AppTheme.Dark;

        public bool IsDark { get; private set; } = true;

        public event EventHandler? ThemeChanged;

        public void Apply(AppTheme theme)
        {
            Theme = theme;

            bool shouldBeDark = theme switch
            {
                AppTheme.Light => false,
                AppTheme.System => _systemTheme.IsDark,
                _ => true
            };

            if (Application.Current is null) return;

            // Re-applying the same palette would still cost a full resource invalidation.
            if (shouldBeDark == IsDark && Application.Current.Resources.MergedDictionaries.Count > 0)
            {
                ThemeChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            IsDark = shouldBeDark;

            ResourceDictionary palette = new()
            {
                Source = shouldBeDark ? DarkPalette : LightPalette
            };

            System.Collections.ObjectModel.Collection<ResourceDictionary> dictionaries =
                Application.Current.Resources.MergedDictionaries;

            if (dictionaries.Count > PaletteDictionaryIndex) dictionaries[PaletteDictionaryIndex] = palette;
            else dictionaries.Insert(PaletteDictionaryIndex, palette);

            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnSystemThemeChanged(object? sender, EventArgs e)
        {
            if (Theme != AppTheme.System) return;

            // The provider polls on a background thread; resource swaps must happen on the UI
            // thread or WPF throws on the first binding that reads the new dictionary.
            Application.Current?.Dispatcher.BeginInvoke(new Action(() => Apply(AppTheme.System)));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _systemTheme.Changed -= OnSystemThemeChanged;
        }
    }
}
