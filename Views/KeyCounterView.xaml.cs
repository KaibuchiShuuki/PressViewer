using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PressViewer.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace PressViewer.Views;

public partial class KeyCounterView : UserControl
{
    private static readonly string[] ViewKeys =
    {
        "SortedTotal",
        "SortedToday",
        "SortedYesterday",
        "SortedDayBefore",
        "SortedSelected",
    };

    private KeyCounterViewModel? _viewModel;

    public KeyCounterView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.SortModeChanged -= OnSortModeChanged;

        _viewModel = e.NewValue as KeyCounterViewModel;

        if (_viewModel is not null)
            _viewModel.SortModeChanged += OnSortModeChanged;

        ApplySort();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.SortModeChanged -= OnSortModeChanged;
    }

    private void OnSortModeChanged(int mode) => ApplySort();

    private void ApplySort()
    {
        var byCount = _viewModel?.SortModeIndex == 1;

        foreach (var key in ViewKeys)
        {
            if (Resources[key] is not CollectionViewSource source)
                continue;

            source.SortDescriptions.Clear();

            if (byCount)
            {
                source.SortDescriptions.Add(new SortDescription(nameof(KeyCountItem.Count), ListSortDirection.Descending));
                source.SortDescriptions.Add(new SortDescription(nameof(KeyCountItem.Key), ListSortDirection.Ascending));
            }
            else
            {
                source.SortDescriptions.Add(new SortDescription(nameof(KeyCountItem.Length), ListSortDirection.Ascending));
                source.SortDescriptions.Add(new SortDescription(nameof(KeyCountItem.Key), ListSortDirection.Ascending));
            }
        }
    }
}
