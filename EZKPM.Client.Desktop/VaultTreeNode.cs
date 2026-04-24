using System.Collections.ObjectModel;
using EZKPM.Shared.Contracts;

namespace EZKPM.Client.Desktop;

public class VaultTreeNode
{
    public VaultAssetPayload Payload { get; set; }
    public ObservableCollection<VaultTreeNode> Children { get; set; } = new();
    public bool IsExpanded { get; set; }

    public string Title => Payload.Title;
    public string Url => Payload.Url;
    public bool HasUrl => !string.IsNullOrWhiteSpace(Payload.Url);
    public bool IsFolder => Payload.AssetType == "Folder";
    
    public string Icon
    {
        get
        {
            return Payload.AssetType switch
            {
                "Folder" => "📁",
                "Login" => "🔑",
                "Passkey" => "🛡️",
                "Payment" => "💳",
                "SSH Key" => "🖥️",
                "SSL Key" => "🔒",
                "API Key" => "🔌",
                "Authenticator" => "⏱️",
                _ => "📄" // SecureNote
            };
        }
    }
}
