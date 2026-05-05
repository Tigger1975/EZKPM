using Avalonia.Controls;
using System.Threading.Tasks;

namespace EZKPM.Client.Desktop.Views;

public partial class SplashScreenWindow : Window
{
    public SplashScreenWindow()
    {
        InitializeComponent();
    }

    public void UpdateStatus(string message, double? progressValue = null)
    {
        var statusText = this.FindControl<TextBlock>("StatusText");
        if (statusText != null) statusText.Text = message;

        var progress = this.FindControl<ProgressBar>("LoadingProgress");
        if (progress != null)
        {
            if (progressValue.HasValue)
            {
                progress.IsIndeterminate = false;
                progress.Value = progressValue.Value;
            }
            else
            {
                progress.IsIndeterminate = true;
            }
        }
    }
}
