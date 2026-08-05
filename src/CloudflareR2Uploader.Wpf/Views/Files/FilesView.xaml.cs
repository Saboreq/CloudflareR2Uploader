using System;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CloudflareR2Uploader.Wpf.ViewModels.Files;

namespace CloudflareR2Uploader.Wpf.Views.Files
{
    /// <summary>
    /// The Files browser view.
    /// <para>
    /// Code-behind is limited to the three things that genuinely cannot be expressed as
    /// bindings: bridging <see cref="DataGrid.SelectedItems"/> (which is not a dependency
    /// property and therefore not bindable), Explorer's right-click selection rule, and
    /// double-click to open a folder.
    /// </para>
    /// </summary>
    public partial class FilesView : UserControl
    {
        private FilesViewModel? _viewModel;
        private bool _syncing;

        public FilesView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Unloaded += OnUnloaded;

            Grid.MouseDoubleClick += OnGridDoubleClick;
            Grid.PreviewMouseRightButtonDown += OnGridRightButtonDown;
            Cards.MouseDoubleClick += OnCardsDoubleClick;
            Cards.PreviewMouseRightButtonDown += OnCardsRightButtonDown;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            Detach();

            _viewModel = e.NewValue as FilesViewModel;
            if (_viewModel is null) return;

            _viewModel.SelectedRows.CollectionChanged += OnViewModelSelectionChanged;
            _viewModel.FilterFocusRequested += OnFilterFocusRequested;
            _viewModel.ClearSelectionRequested += OnClearSelectionRequested;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e) => Detach();

        private void Detach()
        {
            if (_viewModel is null) return;

            _viewModel.SelectedRows.CollectionChanged -= OnViewModelSelectionChanged;
            _viewModel.FilterFocusRequested -= OnFilterFocusRequested;
            _viewModel.ClearSelectionRequested -= OnClearSelectionRequested;
            _viewModel = null;
        }

        // ------------------------------------------------------------- selection bridging

        private void OnGridSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || _viewModel is null) return;

            SyncSelectionFrom(Grid.SelectedItems.Cast<object>());
        }

        private void OnCardsSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || _viewModel is null) return;

            SyncSelectionFrom(Cards.SelectedItems.Cast<object>());
        }

        private void SyncSelectionFrom(System.Collections.Generic.IEnumerable<object> selectedItems)
        {
            if (_viewModel is null) return;

            _syncing = true;
            try
            {
                _viewModel.SelectedRows.Clear();
                foreach (object item in selectedItems)
                {
                    if (item is FileRowViewModel row) _viewModel.SelectedRows.Add(row);
                }

                // Keep the per-row checkboxes in step with the grid's own selection.
                foreach (FileRowViewModel row in _viewModel.Rows)
                {
                    row.IsSelected = _viewModel.SelectedRows.Contains(row);
                }
            }
            finally
            {
                _syncing = false;
            }
        }

        private void OnViewModelSelectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_syncing || _viewModel is null) return;

            _syncing = true;
            try
            {
                Grid.SelectedItems.Clear();
                Cards.SelectedItems.Clear();
                foreach (FileRowViewModel row in _viewModel.SelectedRows)
                {
                    Grid.SelectedItems.Add(row);
                    Cards.SelectedItems.Add(row);
                }
            }
            finally
            {
                _syncing = false;
            }
        }

        private void OnClearSelectionRequested(object? sender, EventArgs e)
        {
            Grid.UnselectAll();
            Cards.UnselectAll();
        }

        private void OnFilterFocusRequested(object? sender, EventArgs e)
        {
            FilterBox.Focus();
            FilterBox.SelectAll();
        }

        // ------------------------------------------------------------------ mouse gestures

        /// <summary>Double-click opens a folder; double-clicking an object does nothing destructive.</summary>
        private void OnGridDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel is null) return;
            if (FindRow(e.OriginalSource as DependencyObject) is null) return;

            if (_viewModel.OpenCommand.CanExecute(null)) _viewModel.OpenCommand.Execute(null);
        }

        private void OnCardsDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel is null || FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is null) return;
            if (_viewModel.OpenCommand.CanExecute(null)) _viewModel.OpenCommand.Execute(null);
        }

        /// <summary>
        /// Explorer's rule: right-clicking inside an existing multi-selection keeps it,
        /// right-clicking outside one selects just that row first. Without this, opening the
        /// context menu on a five-row selection would silently collapse it to one row.
        /// </summary>
        private void OnGridRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            DataGridRow? row = FindRow(e.OriginalSource as DependencyObject);
            if (row is null) return;

            if (row.IsSelected) return;

            Grid.SelectedItems.Clear();
            row.IsSelected = true;
            Grid.CurrentItem = row.Item;
        }

        private void OnCardsRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            ListBoxItem? item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
            if (item is null || item.IsSelected) return;

            Cards.SelectedItems.Clear();
            item.IsSelected = true;
        }

        private void OnRowActionsClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || Grid.ContextMenu is not ContextMenu menu) return;

            DataGridRow? row = FindRow(button);
            if (row is not null && !row.IsSelected)
            {
                Grid.SelectedItems.Clear();
                row.IsSelected = true;
                Grid.CurrentItem = row.Item;
            }

            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
            e.Handled = true;
        }

        private static DataGridRow? FindRow(DependencyObject? source)
            => FindAncestor<DataGridRow>(source);

        private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
        {
            while (source is not null and not T)
            {
                source = source is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(source)
                    : LogicalTreeHelper.GetParent(source);
            }

            return source as T;
        }
    }
}
