using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace EZKPM.Client.Desktop.Views
{
    /// <summary>
    /// Das UI-Fenster, das den Benutzer zwingt, Metadaten für das Payment-Log einzugeben (FA 22).
    /// </summary>
    public partial class AuditDialog : Window
    {
        public class AuditResult
        {
            public bool IsAuthorized { get; set; }
            public string Amount { get; set; }
            public string OrderId { get; set; }
        }

        private TaskCompletionSource<AuditResult> _tcs;

        public AuditDialog()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <summary>
        /// Öffnet das Fenster asynchron und wartet auf die Eingabe des Nutzers.
        /// </summary>
        public Task<AuditResult> ShowAuditPromptAsync()
        {
            _tcs = new TaskCompletionSource<AuditResult>();
            this.Show(); // Öffnet das Fenster als Topmost
            return _tcs.Task;
        }

        private void AuthorizeButton_Click(object sender, RoutedEventArgs e)
        {
            var amount = this.FindControl<TextBox>("AmountTextBox")?.Text;
            var orderId = this.FindControl<TextBox>("OrderIdTextBox")?.Text;

            // Pflichtfelder: Leere Eingaben werden blockiert (FA 22)
            if (string.IsNullOrWhiteSpace(amount) || string.IsNullOrWhiteSpace(orderId))
            {
                // In einer vollen App würde hier noch ein Fehler-Textblock rot aufleuchten
                return;
            }

            _tcs.TrySetResult(new AuditResult
            {
                IsAuthorized = true,
                Amount = amount.Trim(),
                OrderId = orderId.Trim()
            });
            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // Bricht den Autofill ab
            _tcs.TrySetResult(new AuditResult { IsAuthorized = false });
            this.Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            // Fallback: Falls der Nutzer das Fenster einfach über das 'X' schließt, blockieren wir den Zugriff.
            _tcs.TrySetResult(new AuditResult { IsAuthorized = false });
        }
    }
}