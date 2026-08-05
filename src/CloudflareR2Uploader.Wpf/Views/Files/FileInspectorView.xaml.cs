using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace CloudflareR2Uploader.Wpf.Views.Files
{
    public partial class FileInspectorView : UserControl
    {
        public FileInspectorView()
        {
            InitializeComponent();
        }

        private void OnOverflowClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.ContextMenu is not ContextMenu menu) return;

            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
            e.Handled = true;
        }
    }
}
