using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System.Threading.Tasks;

namespace EZKPM.Client.Desktop.Views
{
    public partial class ConfirmationDialog : Window
    {
        private TaskCompletionSource<bool> _tcs;

        public ConfirmationDialog()
        {
            InitializeComponent();
        }

        public ConfirmationDialog(string message) : this()
        {
            var textBlock = this.FindControl<TextBlock>("MessageTextBlock");
            if (textBlock != null)
            {
                textBlock.Text = message;
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public Task<bool> ShowDialogAsync(Window parent)
        {
            _tcs = new TaskCompletionSource<bool>();
            this.ShowDialog(parent);
            return _tcs.Task;
        }

        private void YesButton_Click(object sender, RoutedEventArgs e)
        {
            _tcs.TrySetResult(true);
            this.Close();
        }

        private void NoButton_Click(object sender, RoutedEventArgs e)
        {
            _tcs.TrySetResult(false);
            this.Close();
        }

        protected override void OnClosed(System.EventArgs e)
        {
            base.OnClosed(e);
            _tcs.TrySetResult(false);
        }
    }
}
