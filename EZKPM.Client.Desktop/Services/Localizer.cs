using System.ComponentModel;
using System.Globalization;
using EZKPM.Client.Desktop.Resources; // Namespace deiner AppStrings.resx

namespace EZKPM.Client.Desktop.Services
{
    public class Localizer : INotifyPropertyChanged
    {
        public static Localizer Instance { get; } = new Localizer();
        public event PropertyChangedEventHandler PropertyChanged;

        // Dieser Indexer ermöglicht das dynamische Binding im XAML!
        public string this[string key]
        {
            get
            {
                var translation = AppStrings.ResourceManager.GetString(key, CultureInfo.CurrentUICulture);
                // Wenn der Key nicht existiert, zeigen wir [Key] an (genau wie in Blazor)
                return translation ?? $"[{key}]";
            }
        }

        public void SetLanguage(string cultureCode)
        {
            CultureInfo.CurrentUICulture = new CultureInfo(cultureCode);
            // "Item[]" ist das magische Wort, um Avalonia zu sagen: Lade ALLE Texte im UI neu!
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }
    }
}