using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Threading.Tasks;

namespace EZKPM.Client.Desktop.Views
{
    public partial class SsoApprovalDialog : Window
    {
        public bool Result { get; private set; }

        public SsoApprovalDialog()
        {
            InitializeComponent();
        }

        public SsoApprovalDialog(string appId, string originServerUrl) : this()
        {
            this.FindControl<TextBlock>("AppIdText").Text = string.Format(EZKPM.Client.Desktop.Resources.AppStrings.SsoDialog_AppId, appId);
            this.FindControl<TextBlock>("OriginServerText").Text = string.Format(EZKPM.Client.Desktop.Resources.AppStrings.SsoDialog_Server, originServerUrl);
        }

        private void Approve_Click(object sender, RoutedEventArgs e)
        {
            Result = true;
            Close();
        }

        private void Deny_Click(object sender, RoutedEventArgs e)
        {
            Result = false;
            Close();
        }

        public Task<bool> ShowDialogAsync(Window owner)
        {
            var tcs = new TaskCompletionSource<bool>();
            this.Closed += delegate { tcs.TrySetResult(Result); };
            _ = this.ShowDialog(owner);
            return tcs.Task;
        }
    }
}
