using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace EZKPM.Client.Desktop.Views;

public partial class CsvMappingWindow : Window
{
    private List<string> _csvHeaders;
    public Dictionary<string, int> Mapping { get; private set; } = new();
    public bool IsConfirmed { get; private set; }

    public CsvMappingWindow()
    {
        InitializeComponent();
    }

    public CsvMappingWindow(List<string> csvHeaders) : this()
    {
        _csvHeaders = csvHeaders;
        PopulateComboBoxes();
    }

    private void PopulateComboBoxes()
    {
        var options = new List<string> { "-- Ignorieren --" };
        options.AddRange(_csvHeaders);

        TitleComboBox.ItemsSource = options;
        UsernameComboBox.ItemsSource = options;
        PasswordComboBox.ItemsSource = options;
        UrlComboBox.ItemsSource = options;
        NotesComboBox.ItemsSource = options;

        // Auto-select basic matches
        TitleComboBox.SelectedIndex = FindBestMatch(new[] { "Lieferant", "Account", "Title", "Name", "Titel" });
        UsernameComboBox.SelectedIndex = FindBestMatch(new[] { "Anmelde-ID", "Login Name", "Username", "Benutzer", "Login" });
        PasswordComboBox.SelectedIndex = FindBestMatch(new[] { "Passwort 1", "Passwort", "Password", "Pwd" });
        UrlComboBox.SelectedIndex = FindBestMatch(new[] { "Web-Seite", "Web Site", "Url", "Website", "Link" });
        NotesComboBox.SelectedIndex = FindBestMatch(new[] { "Anmerkung", "Comments", "Notes", "Notizen", "Bemerkung" });
    }

    private int FindBestMatch(string[] possibleNames)
    {
        for (int i = 0; i < _csvHeaders.Count; i++)
        {
            var header = _csvHeaders[i].ToLower();
            if (possibleNames.Any(p => header.Contains(p.ToLower())))
                return i + 1; // +1 because index 0 is "-- Ignorieren --"
        }
        return 0; // Ignore
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Mapping["Title"] = TitleComboBox.SelectedIndex - 1;
        Mapping["Username"] = UsernameComboBox.SelectedIndex - 1;
        Mapping["Password"] = PasswordComboBox.SelectedIndex - 1;
        Mapping["Url"] = UrlComboBox.SelectedIndex - 1;
        Mapping["Notes"] = NotesComboBox.SelectedIndex - 1;

        IsConfirmed = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        Close();
    }
}
