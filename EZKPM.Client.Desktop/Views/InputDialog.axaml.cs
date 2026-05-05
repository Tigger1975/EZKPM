using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System.Threading.Tasks;

namespace EZKPM.Client.Desktop.Views;

public partial class InputDialog : Window
{
    private TaskCompletionSource<string> _tcs;

    public InputDialog()
    {
        InitializeComponent();
    }
    
    public InputDialog(string message, string defaultText = "") : this()
    {
        var textBlock = this.FindControl<TextBlock>("MessageTextBlock");
        if (textBlock != null) textBlock.Text = message;
        
        var textBox = this.FindControl<TextBox>("InputTextBox");
        if (textBox != null) textBox.Text = defaultText;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public Task<string> ShowDialogAsync(Window parent)
    {
        _tcs = new TaskCompletionSource<string>();
        this.ShowDialog(parent);
        return _tcs.Task;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var textBox = this.FindControl<TextBox>("InputTextBox");
        _tcs.TrySetResult(textBox?.Text ?? "");
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
