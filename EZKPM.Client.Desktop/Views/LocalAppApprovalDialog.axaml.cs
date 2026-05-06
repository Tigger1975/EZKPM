using Avalonia.Controls;
using Avalonia.Interactivity;

namespace EZKPM.Client.Desktop.Views
{
    public class ApprovalResult
    {
        public bool IsApproved { get; set; }
        public bool RememberTrust { get; set; }
    }

    public partial class LocalAppApprovalDialog : Window
    {
        public ApprovalResult Result { get; private set; } = new ApprovalResult();

        public LocalAppApprovalDialog()
        {
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
        }

        public LocalAppApprovalDialog(string processName, string assetTitle, string warningText = null) : this()
        {
            var msg = this.FindControl<TextBlock>("MessageText");
            if (msg != null)
            {
                msg.Text = $"Der lokale Prozess '{processName}' fordert Zugriff auf die Anmeldedaten für '{assetTitle}' an.\n\nMöchten Sie dies zulassen?";
            }

            var cb = this.FindControl<CheckBox>("TrustCheckBox");
            var warn = this.FindControl<TextBlock>("WarningText");

            if (!string.IsNullOrEmpty(warningText))
            {
                if (cb != null) cb.IsVisible = false;
                if (warn != null) 
                {
                    warn.Text = warningText;
                    warn.IsVisible = true;
                }
            }
        }

        private void ApproveButton_Click(object sender, RoutedEventArgs e)
        {
            var cb = this.FindControl<CheckBox>("TrustCheckBox");
            Result.IsApproved = true;
            Result.RememberTrust = cb?.IsChecked ?? false;
            this.Close(Result);
        }

        private void DenyButton_Click(object sender, RoutedEventArgs e)
        {
            Result.IsApproved = false;
            Result.RememberTrust = false;
            this.Close(Result);
        }
    }
}
