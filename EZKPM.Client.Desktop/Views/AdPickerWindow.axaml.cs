#nullable enable
using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using EZKPM.Client.Desktop.Services;

namespace EZKPM.Client.Desktop.Views;

public partial class AdPickerWindow : Window
{
    public AdPrincipal? SelectedPrincipal { get; private set; }
    private readonly AdPickerFilterMode _filterMode;

    public AdPickerWindow()
    {
        InitializeComponent();
        _filterMode = AdPickerFilterMode.All;
        _ = PerformSearch(""); // initial load
    }

    public AdPickerWindow(AdPickerFilterMode filterMode)
    {
        InitializeComponent();
        _filterMode = filterMode;
        _ = PerformSearch(""); // initial load
    }

    private async System.Threading.Tasks.Task PerformSearch(string query)
    {
        var results = await AdSearchService.SearchAsync(query, _filterMode);
        ResultsListBox.ItemsSource = results;
    }

    private async void SearchButton_Click(object? sender, RoutedEventArgs e)
    {
        await PerformSearch(SearchTextBox.Text ?? "");
    }

    private async void SearchTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await PerformSearch(SearchTextBox.Text ?? "");
        }
    }

    private void ResultsListBox_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ResultsListBox.SelectedItem is AdPrincipal principal)
        {
            SelectedPrincipal = principal;
            Close(principal);
        }
    }

    private void SelectButton_Click(object? sender, RoutedEventArgs e)
    {
        if (ResultsListBox.SelectedItem is AdPrincipal principal)
        {
            SelectedPrincipal = principal;
            Close(principal);
        }
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
