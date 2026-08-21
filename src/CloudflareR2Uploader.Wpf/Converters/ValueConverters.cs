using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Wpf.Converters
{
    /// <summary>
    /// Boolean to <see cref="Visibility"/>. WPF ships one, but it cannot be inverted, and
    /// half the bindings in this application need the inverse.
    /// </summary>
    public sealed class BooleanToVisibilityConverter : IValueConverter
    {
        /// <summary>When true, <c>true</c> collapses instead of showing.</summary>
        public bool Invert { get; set; }

        /// <summary>Use <see cref="Visibility.Hidden"/> so layout keeps the slot.</summary>
        public bool UseHidden { get; set; }

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool flag = value is bool boolean && boolean;
            if (Invert) flag = !flag;
            return flag ? Visibility.Visible : (UseHidden ? Visibility.Hidden : Visibility.Collapsed);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }

    public sealed class BooleanNegationConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is not bool boolean || !boolean;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is not bool boolean || !boolean;
    }

    /// <summary>Collapses when the bound value is null.</summary>
    public sealed class NullToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool hasValue = value is not null;
            if (Invert) hasValue = !hasValue;
            return hasValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }

    /// <summary>Collapses when the bound string is null, empty or whitespace.</summary>
    public sealed class StringEmptyToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool hasText = !string.IsNullOrWhiteSpace(value as string);
            if (Invert) hasText = !hasText;
            return hasText ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }

    /// <summary>Collapses when a count is zero. Used by badges that must vanish at 0.</summary>
    public sealed class CountToVisibilityConverter : IValueConverter
    {
        /// <summary>When true, a count of zero is what makes the element visible.</summary>
        public bool Invert { get; set; }

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            long count = value switch
            {
                int integer => integer,
                long large => large,
                _ => 0
            };

            bool visible = Invert ? count == 0 : count > 0;
            return visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }

    /// <summary>
    /// Shows an element only while the bound enum equals the parameter. Used to switch the
    /// inspector's tab content without a TabControl, which would otherwise impose its own
    /// header chrome on top of the design's underline tabs.
    /// </summary>
    public sealed class EnumMatchVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is null || parameter is null) return Visibility.Collapsed;

            return string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }

    /// <summary>
    /// Converts a byte count through the shared formatter so every WPF surface displays the
    /// same file size.
    /// </summary>
    public sealed class FileSizeConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value switch
            {
                long bytes => FileSizeFormatter.Format(bytes),
                int bytes => FileSizeFormatter.Format(bytes),
                null => string.Empty,
                _ => string.Empty
            };
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }

    /// <summary>
    /// UTC to local display time. Everything is stored and compared in UTC; only the UI ever
    /// converts, and it does so here so the format is defined in exactly one place.
    /// </summary>
    public sealed class LocalTimeConverter : IValueConverter
    {
        public const string DateTimeFormat = "d MMM yyyy, HH:mm";
        public const string TimeOnlyFormat = "HH:mm:ss";

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not DateTime utc) return string.Empty;
            if (utc == DateTime.MinValue) return string.Empty;

            DateTime local = utc.Kind == DateTimeKind.Utc ? utc.ToLocalTime() : utc;
            string format = string.Equals(parameter as string, "time", StringComparison.OrdinalIgnoreCase)
                ? TimeOnlyFormat
                : DateTimeFormat;

            return local.ToString(format, culture);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }

    /// <summary>
    /// Scales a 0–100 percentage into a pixel width. Used by the multi-selection breakdown
    /// bars, which are drawn as plain rectangles rather than as ProgressBars because they
    /// are decoration, not progress, and must not be announced as such.
    /// </summary>
    public sealed class PercentToWidthConverter : IMultiValueConverter
    {
        public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Length < 2) return 0d;
            if (values[0] is not double percent || values[1] is not double available) return 0d;
            if (double.IsNaN(available) || available <= 0) return 0d;

            double fraction = Math.Clamp(percent / 100d, 0d, 1d);
            return Math.Round(available * fraction);
        }

        public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// Looks a resource up by key at bind time.
    /// <para>
    /// Row icons are chosen per item, so the geometry cannot be a compile-time
    /// <c>StaticResource</c>. A view model must not hold WPF <c>Geometry</c> objects, so it
    /// exposes the key instead and this resolves it against the application's resources.
    /// </para>
    /// </summary>
    public sealed class ResourceLookupConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not string key || key.Length == 0) return null;
            return Application.Current?.TryFindResource(key);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }

    /// <summary>
    /// True when the bound enum equals the parameter. Used to drive RadioButton.IsChecked for
    /// segmented controls and tabs without one boolean property per option.
    /// </summary>
    public sealed class EnumMatchConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is null || parameter is null) return false;
            return string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            // Only a checked radio button writes back; an unchecked one must not clear the
            // selection, or clicking any option would briefly select nothing.
            if (value is not bool isChecked || !isChecked || parameter is null) return Binding.DoNothing;

            try
            {
                return Enum.Parse(targetType, parameter.ToString()!, ignoreCase: true);
            }
            catch (ArgumentException)
            {
                return Binding.DoNothing;
            }
        }
    }
}
