using System.Collections.ObjectModel;
using EZKPM.Shared.Contracts;

namespace EZKPM.Client.Desktop;

public class VaultTreeNode
{
    public VaultAssetPayload Payload { get; set; }
    public ObservableCollection<VaultTreeNode> Children { get; set; } = new();

    public string Title => Payload.Title;
    public bool IsFolder => Payload.AssetType == "Folder";
    
    public string Icon
    {
        get
        {
            if (Payload.AssetType == "Folder") return "📁";
            if (Payload.AssetType == "Login") return "🔑";
            if (Payload.AssetType == "Payment") return "💳";
            return "📄"; // SecureNote
        }
    }
}
