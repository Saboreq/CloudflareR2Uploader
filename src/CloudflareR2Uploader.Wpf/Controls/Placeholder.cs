using System.Windows;
using System.Windows.Input;

namespace CloudflareR2Uploader.Wpf.Controls
{
    /// <summary>
    /// Placeholder text and an optional clear command for text inputs.
    /// <para>
    /// <see cref="System.Windows.Controls.TextBox"/> has no placeholder of its own, and
    /// WPF's <c>TextBoxBase</c> gives no hook for one, so the templates in
    /// <c>Controls.Inputs.xaml</c> read these attached properties instead.
    /// </para>
    /// </summary>
    public static class Placeholder
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
            "Text",
            typeof(string),
            typeof(Placeholder),
            new FrameworkPropertyMetadata(string.Empty));

        public static string GetText(DependencyObject element) => (string)element.GetValue(TextProperty);

        public static void SetText(DependencyObject element, string value) => element.SetValue(TextProperty, value);

        /// <summary>Invoked by the search box's trailing clear button.</summary>
        public static readonly DependencyProperty ClearCommandProperty = DependencyProperty.RegisterAttached(
            "ClearCommand",
            typeof(ICommand),
            typeof(Placeholder),
            new FrameworkPropertyMetadata(null));

        public static ICommand? GetClearCommand(DependencyObject element) =>
            (ICommand?)element.GetValue(ClearCommandProperty);

        public static void SetClearCommand(DependencyObject element, ICommand? value) =>
            element.SetValue(ClearCommandProperty, value);
    }
}
