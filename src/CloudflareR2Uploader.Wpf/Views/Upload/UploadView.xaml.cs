using System;
using System.Windows;
using System.Windows.Controls;
using CloudflareR2Uploader.Wpf.ViewModels.Upload;

namespace CloudflareR2Uploader.Wpf.Views.Upload
{
    /// <summary>
    /// The Upload screen.
    /// <para>
    /// Code-behind adapts WPF's drag-and-drop events, which carry a <see cref="DataObject"/>
    /// that a view model must not touch, into a plain list of paths.
    /// </para>
    /// </summary>
    public partial class UploadView : UserControl
    {
        public UploadView()
        {
            InitializeComponent();

            DragEnter += OnDragEnter;
            DragOver += OnDragOver;
            DragLeave += OnDragLeave;
            Drop += OnDrop;
        }

        private UploadViewModel? ViewModel => DataContext as UploadViewModel;

        private static bool HasPaths(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop);

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (ViewModel is null) return;
            ViewModel.IsDragOver = HasPaths(e);
            Update(e);
        }

        private void OnDragOver(object sender, DragEventArgs e) => Update(e);

        private void OnDragLeave(object sender, DragEventArgs e)
        {
            if (ViewModel is not null) ViewModel.IsDragOver = false;
        }

        private static void Update(DragEventArgs e)
        {
            e.Effects = HasPaths(e) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (ViewModel is null) return;

            ViewModel.IsDragOver = false;
            e.Handled = true;

            if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;

            ViewModel.AddPaths(paths);
        }
    }
}
