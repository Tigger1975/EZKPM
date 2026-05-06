using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using EZKPM.Shared.Contracts;

namespace EZKPM.Client.Desktop.Views
{
    public partial class AutoTypePromptWindow : Window
    {
        public VaultAssetPayload SelectedAsset { get; private set; }

        public AutoTypePromptWindow()
        {
            InitializeComponent();
        }

        public AutoTypePromptWindow(List<VaultAssetPayload> matches) : this()
        {
            var listBox = this.FindControl<ListBox>("MatchesListBox");
            if (listBox != null)
            {
                listBox.ItemsSource = matches;
                if (matches.Count > 0)
                {
                    listBox.SelectedIndex = 0;
                }
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void BtnExecute_Click(object sender, RoutedEventArgs e)
        {
            var listBox = this.FindControl<ListBox>("MatchesListBox");
            if (listBox != null && listBox.SelectedItem is VaultAssetPayload payload)
            {
                SelectedAsset = payload;
                this.Close(payload);
            }
        }
    }
}
