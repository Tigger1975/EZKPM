using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace EZKPM.Client.Desktop.Views
{
    public class PasswordPromptResult
    {
        public string Password { get; set; }
        public string KeyFilePath { get; set; }
    }

    public partial class PasswordPromptDialog : Window
    {
        private TaskCompletionSource<PasswordPromptResult> _tcs;
        private string _selectedKeyFilePath = null;

        public PasswordPromptDialog()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public Task<PasswordPromptResult> ShowDialogAsync(Window parent)
        {
            _tcs = new TaskCompletionSource<PasswordPromptResult>();
            this.ShowDialog(parent);
            return _tcs.Task;
        }

        private async void SelectKeyFile_Click(object sender, RoutedEventArgs e)
        {
            var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Key-Datei auswählen",
                AllowMultiple = false
            });

            if (files.Count > 0)
            {
                _selectedKeyFilePath = files[0].Path.LocalPath;
                this.FindControl<TextBlock>("KeyFilePathTextBlock").Text = files[0].Name;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            _tcs.TrySetResult(new PasswordPromptResult
            {
                Password = this.FindControl<TextBox>("PasswordTextBox").Text,
                KeyFilePath = _selectedKeyFilePath
            });
            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _tcs.TrySetResult(null);
            this.Close();
        }

        protected override void OnClosed(System.EventArgs e)
        {
            base.OnClosed(e);
            _tcs.TrySetResult(null);
        }
    }
}
