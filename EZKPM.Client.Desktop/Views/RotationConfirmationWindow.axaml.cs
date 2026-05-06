using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;

namespace EZKPM.Client.Desktop.Views
{
    public partial class RotationConfirmationWindow : Window
    {
        public bool IsConfirmed { get; private set; }
        private readonly string _generatedPassword;

        public RotationConfirmationWindow()
        {
            InitializeComponent();
        }

        public RotationConfirmationWindow(string generatedPassword) : this()
        {
            _generatedPassword = generatedPassword;
            var pwBox = this.FindControl<TextBox>("NewPasswordBox");
            if (pwBox != null)
            {
                pwBox.Text = generatedPassword;
            }
        }

        private async void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(_generatedPassword);
                if (sender is Button btn)
                {
                    btn.Content = "✅ Kopiert!";
                    await Task.Delay(2000);
                    btn.Content = "📋 In Zwischenablage kopieren";
                }
            }
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = true;
            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            this.Close();
        }
    }
}
