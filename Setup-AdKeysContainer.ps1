param(
    [string]$DomainPath = "",
    [string]$ContainerName = "EZKPM-Keys"
)

Import-Module ActiveDirectory

if ([string]::IsNullOrWhiteSpace($DomainPath)) {
    $DomainPath = (Get-ADDomain).DistinguishedName
}

$targetOU = "OU=$ContainerName,$DomainPath"

Write-Host "Creating Zero-Knowledge Backup Container: $targetOU"

# 1. Create OU
if (-not (Get-ADOrganizationalUnit -Filter "Name -eq '$ContainerName'")) {
    New-ADOrganizationalUnit -Name $ContainerName -Path $DomainPath
    Write-Host "[+] OU created: $targetOU"
} else {
    Write-Host "[!] OU already exists: $targetOU"
}

# 2. Get ACL
$ou = Get-ADOrganizationalUnit -Identity $targetOU
$aclPath = "AD:\$($ou.DistinguishedName)"
$acl = Get-Acl -Path $aclPath

# 3. Disable Inheritance & Clear Rules
# Protect ACL (True) and Do NOT copy inherited rules (False)
$acl.SetAccessRuleProtection($true, $false)

# Remove all existing rules to start completely fresh
foreach ($rule in $acl.Access) {
    $acl.RemoveAccessRule($rule) | Out-Null
}

$domainAdmins = (Get-ADGroup "Domain Admins").SID
$domainComputers = (Get-ADGroup "Domain Computers").SID
$creatorOwner = New-Object System.Security.Principal.SecurityIdentifier("S-1-3-0") # CREATOR OWNER
$systemSid = New-Object System.Security.Principal.SecurityIdentifier("S-1-5-18") # LOCAL SYSTEM (for DC local access if needed)

# 4. Define Rules

# 4a. Domain Computers: Create Child Objects (Allow)
$ruleComputers = New-Object System.DirectoryServices.ActiveDirectoryAccessRule(
    $domainComputers, 
    [System.DirectoryServices.ActiveDirectoryRights]::CreateChild, 
    [System.Security.AccessControl.AccessControlType]::Allow, 
    [Guid]"00000000-0000-0000-0000-000000000000"
)
$acl.AddAccessRule($ruleComputers)

# 4b. CREATOR OWNER: Full Control (Allow)
# So the machine that created the object can read and update it.
$ruleCreator = New-Object System.DirectoryServices.ActiveDirectoryAccessRule(
    $creatorOwner, 
    [System.DirectoryServices.ActiveDirectoryRights]::GenericAll, 
    [System.Security.AccessControl.AccessControlType]::Allow,
    [Guid]"00000000-0000-0000-0000-000000000000",
    [System.DirectoryServices.ActiveDirectorySecurityInheritance]::All,
    [Guid]"00000000-0000-0000-0000-000000000000"
)
$acl.AddAccessRule($ruleCreator)

# 4c. Domain Admins: Delete/List (Allow)
# Domain Admins need to be able to see the objects exist and delete them if a machine is decommissioned.
$ruleAdminsDelete = New-Object System.DirectoryServices.ActiveDirectoryAccessRule(
    $domainAdmins, 
    [System.DirectoryServices.ActiveDirectoryRights]::DeleteChild -bor [System.DirectoryServices.ActiveDirectoryRights]::ListChildren, 
    [System.Security.AccessControl.AccessControlType]::Allow
)
$acl.AddAccessRule($ruleAdminsDelete)

# 4d. Domain Admins: Read Property (DENY) -> "Die Admins sollen die Inhalte nicht lesen können"
# Wir verbieten explizit das Lesen der Attribute.
$ruleAdminsDenyRead = New-Object System.DirectoryServices.ActiveDirectoryAccessRule(
    $domainAdmins, 
    [System.DirectoryServices.ActiveDirectoryRights]::ReadProperty, 
    [System.Security.AccessControl.AccessControlType]::Deny
)
$acl.AddAccessRule($ruleAdminsDenyRead)

# 5. Apply ACL
Set-Acl -Path $aclPath -AclObject $acl
Write-Host "[+] Blind Drop ACLs erfolgreich angewendet."
Write-Host "    -> Maschinen dürfen Backups (Child Objects) anlegen."
Write-Host "    -> CREATOR OWNER (die Maschine) hat Vollzugriff."
Write-Host "    -> Domain Admins dürfen Objekte auflisten und löschen."
Write-Host "    -> Domain Admins wird das Lesen von Attributen EXPLIZIT VERWEIGERT."
