$axamlFile = "c:\Users\adm-kh\source\repos\EZKPM\EZKPM.Client.Desktop\Views\AssetEditorWindow.axaml"
$resxFile = "c:\Users\adm-kh\source\repos\EZKPM\EZKPM.Client.Desktop\Resources\AppStrings.resx"
$designerFile = "c:\Users\adm-kh\source\repos\EZKPM\EZKPM.Client.Desktop\Resources\AppStrings.Designer.cs"

$stringsToAdd = @{
    "AssetEditor_TitleDetails" = "Asset Details"
    "AssetEditor_PopOut" = "Pop Out Editor"
    "AssetEditor_Duplicate" = "Duplicate Asset"
    "AssetEditor_SaveNew" = "Save and New"
    "AssetEditor_SaveClose" = "Save and Close"
    "AssetEditor_LblTitle" = "Title"
    "AssetEditor_PhTitle" = "e.g. My Bank Account"
    "AssetEditor_LblType" = "Asset Type"
    "AssetEditor_LblParent" = "Parent Folder"
    "AssetEditor_LblUsername" = "Username / Email"
    "AssetEditor_PhUsername" = "user@example.com"
    "AssetEditor_TipCopy" = "Copy"
    "AssetEditor_LblPassword" = "Password"
    "AssetEditor_TipGenerate" = "Generate"
    "AssetEditor_ShowPassword" = "Show Password"
    "AssetEditor_LblPaymentMethod" = "Payment Method"
    "AssetEditor_PayCard" = "Credit / Debit Card"
    "AssetEditor_PayService" = "Payment Service (e.g. PayPal)"
    "AssetEditor_LblCardHolder" = "Card Holder / Account Name"
    "AssetEditor_PhCardHolder" = "Max Mustermann"
    "AssetEditor_LblExpiry" = "Expiry (MM/YY)"
    "AssetEditor_LblCvc" = "CVC / PIN"
    "AssetEditor_LblPrimaryFile" = "Primary File (Certificate / Key / Container)"
    "AssetEditor_PhNoFile" = "No file selected..."
    "AssetEditor_TipBrowse" = "Browse..."
    "AssetEditor_TipClear" = "Clear"
    "AssetEditor_TipDownload" = "Download"
    "AssetEditor_LblSecondaryFile" = "Secondary File (e.g. Public Key / Cert)"
    "AssetEditor_HdrGenerator" = "Password Generator Settings"
    "AssetEditor_LblLength" = "Length"
    "AssetEditor_ChkUpper" = "Uppercase (A-Z)"
    "AssetEditor_ChkLower" = "Lowercase (a-z)"
    "AssetEditor_ChkNumbers" = "Numbers (0-9)"
    "AssetEditor_ChkSymbols" = "Symbols (!@#$)"
    "AssetEditor_LblTotpSecret" = "TOTP Secret Key (Base32)"
    "AssetEditor_ChkShowSecret" = "Show Secret"
    "AssetEditor_LblCurrentCode" = "Current Code:"
    "AssetEditor_TabGeneral" = "General &amp; Notes"
    "AssetEditor_LblLoginUrl" = "Login URL"
    "AssetEditor_LblSecureNotes" = "Secure Notes"
    "AssetEditor_PhNotes" = "Additional information..."
    "AssetEditor_LblDetailedDesc" = "Detailed Description"
    "AssetEditor_TabVars" = "Custom Variables"
    "AssetEditor_PhUserPrompt" = "(User Prompt)"
    "AssetEditor_TipAddVar" = "Add Variable"
    "AssetEditor_TabAcl" = "Permissions (ACL)"
    "AssetEditor_AclDeny" = "Deny (-1)"
    "AssetEditor_AclExec" = "Execute (1)"
    "AssetEditor_AclRead" = "Read (2)"
    "AssetEditor_AclOwner" = "Owner (3)"
    "AssetEditor_TabAttachments" = "Attachments"
    "AssetEditor_TipAddFile" = "Add File(s)..."
    "AssetEditor_TipDlSel" = "Download Selected"
    "AssetEditor_TipRmSel" = "Remove Selected"
    "AssetEditor_TabSettings" = "Settings &amp; Expiry"
    "AssetEditor_LblValidity" = "Password Validity (Days, max 365)"
    "AssetEditor_HdrAuto" = "Login Automation Settings (Browser Extension)"
    "AssetEditor_LblMethod" = "Login Method"
    "AssetEditor_ChkAutoLearn" = "Enable AutoLearn Mode (Observe DOM)"
    "AssetEditor_LblDom" = "Learned DOM Selectors"
    "AssetEditor_PhUserSel" = "Username Field Selector"
    "AssetEditor_PhPassSel" = "Password Field Selector"
    "AssetEditor_PhNextSel" = "Next Button Selector"
    "AssetEditor_PhSubSel" = "Submit Button Selector"
    "AssetEditor_ComboAuto" = "AutoLearn"
    "AssetEditor_ComboOne" = "OneStep (Username &amp; Password)"
    "AssetEditor_ComboTwo" = "TwoStep (Username -&gt; Next -&gt; Password)"
    "AssetEditor_ComboBasic" = "BasicAuth"
}

# Update resx
[xml]$resx = Get-Content $resxFile
foreach ($key in $stringsToAdd.Keys) {
    if (-not ($resx.root.data | Where-Object { $_.name -eq $key })) {
        $newNode = $resx.CreateElement("data")
        $newNode.SetAttribute("name", $key)
        $newNode.SetAttribute("xml:space", "preserve")
        $valNode = $resx.CreateElement("value")
        $plainText = $stringsToAdd[$key].Replace("&amp;", "&").Replace("&gt;", ">")
        $valNode.InnerText = $plainText
        $newNode.AppendChild($valNode) > $null
        $resx.root.AppendChild($newNode) > $null
    }
}
$resx.Save($resxFile)

# Update Designer.cs
$designerContent = Get-Content $designerFile -Raw

# find the last "    }" and replace it with properties, then re-append "    }`r`n}"
$lastIndex = $designerContent.LastIndexOf("    }")
if ($lastIndex -gt 0) {
    $before = $designerContent.Substring(0, $lastIndex)
    $after = $designerContent.Substring($lastIndex)
    
    $props = ""
    foreach ($key in $stringsToAdd.Keys) {
        if (-not $designerContent.Contains("public static string $key")) {
            $plainText = $stringsToAdd[$key].Replace("&amp;", "&").Replace("&gt;", ">")
            $props += @"
        /// <summary>
        ///   Looks up a localized string similar to $plainText.
        /// </summary>
        public static string $key {
            get {
                return ResourceManager.GetString("$key", resourceCulture);
            }
        }

"@
        }
    }
    
    $designerContent = $before + $props + $after
    Set-Content -Path $designerFile -Value $designerContent -NoNewline
}

# Replace in axaml
$axamlContent = Get-Content $axamlFile -Raw

# We must use Regex carefully because some strings might appear multiple times or partially. 
$axamlContent = $axamlContent -replace 'Text="Asset Details"', 'Text="{x:Static res:AppStrings.AssetEditor_TitleDetails}"'
$axamlContent = $axamlContent -replace 'ToolTip\.Tip="Pop Out Editor"', 'ToolTip.Tip="{x:Static res:AppStrings.AssetEditor_PopOut}"'
$axamlContent = $axamlContent -replace 'ToolTip\.Tip="Duplicate Asset"', 'ToolTip.Tip="{x:Static res:AppStrings.AssetEditor_Duplicate}"'
$axamlContent = $axamlContent -replace 'ToolTip\.Tip="Save and New"', 'ToolTip.Tip="{x:Static res:AppStrings.AssetEditor_SaveNew}"'
$axamlContent = $axamlContent -replace 'ToolTip\.Tip="Save and Close"', 'ToolTip.Tip="{x:Static res:AppStrings.AssetEditor_SaveClose}"'
$axamlContent = $axamlContent -replace 'Text="Title"', 'Text="{x:Static res:AppStrings.AssetEditor_LblTitle}"'
$axamlContent = $axamlContent -replace 'PlaceholderText="e\.g\. My Bank Account"', 'PlaceholderText="{x:Static res:AppStrings.AssetEditor_PhTitle}"'
$axamlContent = $axamlContent -replace 'Text="Asset Type"', 'Text="{x:Static res:AppStrings.AssetEditor_LblType}"'
$axamlContent = $axamlContent -replace 'Text="Parent Folder"', 'Text="{x:Static res:AppStrings.AssetEditor_LblParent}"'
$axamlContent = $axamlContent -replace 'Text="Username / Email"', 'Text="{x:Static res:AppStrings.AssetEditor_LblUsername}"'
$axamlContent = $axamlContent -replace 'PlaceholderText="user@example\.com"', 'PlaceholderText="{x:Static res:AppStrings.AssetEditor_PhUsername}"'
$axamlContent = $axamlContent -replace 'ToolTip\.Tip="Copy"', 'ToolTip.Tip="{x:Static res:AppStrings.AssetEditor_TipCopy}"'
$axamlContent = $axamlContent -replace 'Text="Password"', 'Text="{x:Static res:AppStrings.AssetEditor_LblPassword}"'
$axamlContent = $axamlContent -replace 'ToolTip\.Tip="Generate"', 'ToolTip.Tip="{x:Static res:AppStrings.AssetEditor_TipGenerate}"'
$axamlContent = $axamlContent -replace 'Content="Show Password"', 'Content="{x:Static res:AppStrings.AssetEditor_ShowPassword}"'
$axamlContent = $axamlContent -replace 'Text="Payment Method"', 'Text="{x:Static res:AppStrings.AssetEditor_LblPaymentMethod}"'
$axamlContent = $axamlContent -replace '>Credit / Debit Card<', '>{x:Static res:AppStrings.AssetEditor_PayCard}<'
$axamlContent = $axamlContent -replace '>Payment Service \(e\.g\. PayPal\)<', '>{x:Static res:AppStrings.AssetEditor_PayService}<'
$axamlContent = $axamlContent -replace 'Text="Card Holder / Account Name"', 'Text="{x:Static res:AppStrings.AssetEditor_LblCardHolder}"'
$axamlContent = $axamlContent -replace 'PlaceholderText="Max Mustermann"', 'PlaceholderText="{x:Static res:AppStrings.AssetEditor_PhCardHolder}"'
$axamlContent = $axamlContent -replace 'Text="Expiry \(MM/YY\)"', 'Text="{x:Static res:AppStrings.AssetEditor_LblExpiry}"'
$axamlContent = $axamlContent -replace 'Text="CVC / PIN"', 'Text="{x:Static res:AppStrings.AssetEditor_LblCvc}"'
$axamlContent = $axamlContent -replace 'Text="Primary File \(Certificate / Key / Container\)"', 'Text="{x:Static res:AppStrings.AssetEditor_LblPrimaryFile}"'
$axamlContent = $axamlContent -replace 'PlaceholderText="No file selected\.\.\."', 'PlaceholderText="{x:Static res:AppStrings.AssetEditor_PhNoFile}"'
$axamlContent = $axamlContent -replace 'ToolTip\.Tip="Browse\.\.\."', 'ToolTip.Tip="{x:Static res:AppStrings.AssetEditor_TipBrowse}"'
$axamlContent = $axamlContent -replace 'ToolTip\.Tip="Clear"', 'ToolTip.Tip="{x:Static res:AppStrings.AssetEditor_TipClear}"'
$axamlContent = $axamlContent -replace 'ToolTip\.Tip="Download"', 'ToolTip.Tip="{x:Static res:AppStrings.AssetEditor_TipDownload}"'
$axamlContent = $axamlContent -replace 'Text="Secondary File \(e\.g\. Public Key / Cert\)"', 'Text="{x:Static res:AppStrings.AssetEditor_LblSecondaryFile}"'
$axamlContent = $axamlContent -replace 'Header="Password Generator Settings"', 'Header="{x:Static res:AppStrings.AssetEditor_HdrGenerator}"'
$axamlContent = $axamlContent -replace 'Text="Length"', 'Text="{x:Static res:AppStrings.AssetEditor_LblLength}"'
$axamlContent = $axamlContent -replace 'Content="Uppercase \(A-Z\)"', 'Content="{x:Static res:AppStrings.AssetEditor_ChkUpper}"'
$axamlContent = $axamlContent -replace 'Content="Lowercase \(a-z\)"', 'Content="{x:Static res:AppStrings.AssetEditor_ChkLower}"'
$axamlContent = $axamlContent -replace 'Content="Numbers \(0-9\)"', 'Content="{x:Static res:AppStrings.AssetEditor_ChkNumbers}"'
$axamlContent = $axamlContent -replace 'Content="Symbols \(\!@#\$\)"', 'Content="{x:Static res:AppStrings.AssetEditor_ChkSymbols}"'
$axamlContent = $axamlContent -replace 'Text="TOTP Secret Key \(Base32\)"', 'Text="{x:Static res:AppStrings.AssetEditor_LblTotpSecret}"'
$axamlContent = $axamlContent -replace 'Content="Show Secret"', 'Content="{x:Static res:AppStrings.AssetEditor_ChkShowSecret}"'
$axamlContent = $axamlContent -replace 'Text="Current Code:"', 'Text="{x:Static res:AppStrings.AssetEditor_LblCurrentCode}"'
$axamlContent = $axamlContent -replace 'Header="General &amp; Notes"', 'Header="{x:Static res:AppStrings.AssetEditor_TabGeneral}"'
$axamlContent = $axamlContent -replace 'Text="Login URL"', 'Text="{x:Static res:AppStrings.AssetEditor_LblLoginUrl}"'
$axamlContent = $axamlContent -replace 'Text="Secure Notes"', 'Text="{x:Static res:AppStrings.AssetEditor_LblSecureNotes}"'
$axamlContent = $axamlContent -replace 'PlaceholderText="Additional information\.\.\."', 'PlaceholderText="{x:Static res:AppStrings.AssetEditor_PhNotes}"'
$axamlContent = $axamlContent -replace 'Text="Detailed Description"', 'Text="{x:Static res:AppStrings.AssetEditor_LblDetailedDesc}"'
$axamlContent = $axamlContent -replace 'Header="Custom Variables"', 'Header="{x:Static res:AppStrings.AssetEditor_TabVars}"'
$axamlContent = $axamlContent -replace 'PlaceholderText="\(User Prompt\)"', 'PlaceholderText="{x:Static res:AppStrings.AssetEditor_PhUserPrompt}"'
$axamlContent = $axamlContent -replace 'ToolTip\.Tip="Add Variable"', 'ToolTip.Tip="{x:Static res:AppStrings.AssetEditor_TipAddVar}"'
$axamlContent = $axamlContent -replace 'Header="Permissions \(ACL\)"', 'Header="{x:Static res:AppStrings.AssetEditor_TabAcl}"'
$axamlContent = $axamlContent -replace 'Content="Deny \(-1\)"', 'Content="{x:Static res:AppStrings.AssetEditor_AclDeny}"'
$axamlContent = $axamlContent -replace 'Content="Execute \(1\)"', 'Content="{x:Static res:AppStrings.AssetEditor_AclExec}"'
$axamlContent = $axamlContent -replace 'Content="Read \(2\)"', 'Content="{x:Static res:AppStrings.AssetEditor_AclRead}"'
$axamlContent = $axamlContent -replace 'Content="Owner \(3\)"', 'Content="{x:Static res:AppStrings.AssetEditor_AclOwner}"'
$axamlContent = $axamlContent -replace 'Header="Attachments"', 'Header="{x:Static res:AppStrings.AssetEditor_TabAttachments}"'
$axamlContent = $axamlContent -replace 'ToolTip\.Tip="Add File\(s\)\.\.\."', 'ToolTip.Tip="{x:Static res:AppStrings.AssetEditor_TipAddFile}"'
$axamlContent = $axamlContent -replace 'ToolTip\.Tip="Download Selected"', 'ToolTip.Tip="{x:Static res:AppStrings.AssetEditor_TipDlSel}"'
$axamlContent = $axamlContent -replace 'ToolTip\.Tip="Remove Selected"', 'ToolTip.Tip="{x:Static res:AppStrings.AssetEditor_TipRmSel}"'
$axamlContent = $axamlContent -replace 'Header="Settings &amp; Expiry"', 'Header="{x:Static res:AppStrings.AssetEditor_TabSettings}"'
$axamlContent = $axamlContent -replace 'Text="Password Validity \(Days, max 365\)"', 'Text="{x:Static res:AppStrings.AssetEditor_LblValidity}"'
$axamlContent = $axamlContent -replace 'Header="Login Automation Settings \(Browser Extension\)"', 'Header="{x:Static res:AppStrings.AssetEditor_HdrAuto}"'
$axamlContent = $axamlContent -replace 'Text="Login Method"', 'Text="{x:Static res:AppStrings.AssetEditor_LblMethod}"'
$axamlContent = $axamlContent -replace 'Content="Enable AutoLearn Mode \(Observe DOM\)"', 'Content="{x:Static res:AppStrings.AssetEditor_ChkAutoLearn}"'
$axamlContent = $axamlContent -replace 'Text="Learned DOM Selectors"', 'Text="{x:Static res:AppStrings.AssetEditor_LblDom}"'
$axamlContent = $axamlContent -replace 'PlaceholderText="Username Field Selector"', 'PlaceholderText="{x:Static res:AppStrings.AssetEditor_PhUserSel}"'
$axamlContent = $axamlContent -replace 'PlaceholderText="Password Field Selector"', 'PlaceholderText="{x:Static res:AppStrings.AssetEditor_PhPassSel}"'
$axamlContent = $axamlContent -replace 'PlaceholderText="Next Button Selector"', 'PlaceholderText="{x:Static res:AppStrings.AssetEditor_PhNextSel}"'
$axamlContent = $axamlContent -replace 'PlaceholderText="Submit Button Selector"', 'PlaceholderText="{x:Static res:AppStrings.AssetEditor_PhSubSel}"'
$axamlContent = $axamlContent -replace '>AutoLearn<', '>{x:Static res:AppStrings.AssetEditor_ComboAuto}<'
$axamlContent = $axamlContent -replace '>OneStep \(Username &amp; Password\)<', '>{x:Static res:AppStrings.AssetEditor_ComboOne}<'
$axamlContent = $axamlContent -replace '>TwoStep \(Username -&gt; Next -&gt; Password\)<', '>{x:Static res:AppStrings.AssetEditor_ComboTwo}<'
$axamlContent = $axamlContent -replace '>BasicAuth<', '>{x:Static res:AppStrings.AssetEditor_ComboBasic}<'

Set-Content -Path $axamlFile -Value $axamlContent -NoNewline
