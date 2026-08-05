using System.Windows;
using System.Windows.Media;

namespace CloudflareR2Uploader.Wpf.Controls
{
    /// <summary>
    /// Carries an icon geometry on any control so a single control template can render any
    /// glyph.
    /// <para>
    /// This exists instead of putting a <see cref="System.Windows.Shapes.Path"/> into each
    /// button's <c>Content</c> because content-based icons cannot be restyled per state
    /// (hover, pressed, disabled) without duplicating the visual in every call site.
    /// </para>
    /// </summary>
    public static class Icon
    {
        public static readonly DependencyProperty DataProperty = DependencyProperty.RegisterAttached(
            "Data",
            typeof(Geometry),
            typeof(Icon),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static Geometry? GetData(DependencyObject element) => (Geometry?)element.GetValue(DataProperty);

        public static void SetData(DependencyObject element, Geometry? value) => element.SetValue(DataProperty, value);

        /// <summary>Secondary glyph, used where a control shows two icons (for example a badge).</summary>
        public static readonly DependencyProperty SecondaryDataProperty = DependencyProperty.RegisterAttached(
            "SecondaryData",
            typeof(Geometry),
            typeof(Icon),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static Geometry? GetSecondaryData(DependencyObject element) =>
            (Geometry?)element.GetValue(SecondaryDataProperty);

        public static void SetSecondaryData(DependencyObject element, Geometry? value) =>
            element.SetValue(SecondaryDataProperty, value);
    }
}
