# Run this script AS ADMINISTRATOR
$ErrorActionPreference = "Stop"

# 1. Parameter definieren
$SiteName = "EZKPM_Server"
$AppPoolName = "EZKPM_AppPool"
$Port = 8080
$SourcePath = "C:\Users\adm-kh\source\repos\EZKPM\Publish\Server"
$DestPath = "C:\inetpub\EZKPM"

Write-Host "Starte IIS-Deployment für EZKPM..." -ForegroundColor Cyan

# 2. Prüfen ob Admin-Rechte vorliegen
$wid = [System.Security.Principal.WindowsIdentity]::GetCurrent()
$prp = new-object System.Security.Principal.WindowsPrincipal($wid)
if (-not $prp.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "FEHLER: Dieses Skript muss zwingend als Administrator (Elevated) ausgeführt werden!"
    exit
}

# WebAdministration Modul laden
Import-Module WebAdministration

# 3. Verzeichnis vorbereiten
Write-Host "Kopiere Dateien nach $DestPath..."
if (Test-Path $DestPath) {
    Remove-Item -Path "$DestPath\*" -Recurse -Force
} else {
    New-Item -ItemType Directory -Force -Path $DestPath | Out-Null
}
Copy-Item -Path "$SourcePath\*" -Destination $DestPath -Recurse -Force

# 4. AppPool erstellen (No Managed Code für ASP.NET Core)
if (Test-Path "IIS:\AppPools\$AppPoolName") {
    Write-Host "AppPool $AppPoolName existiert bereits, recycle..."
    Restart-WebAppPool $AppPoolName
} else {
    Write-Host "Erstelle AppPool $AppPoolName..."
    New-WebAppPool -Name $AppPoolName
    Set-ItemProperty -Path "IIS:\AppPools\$AppPoolName" -Name "managedRuntimeVersion" -Value ""
    Set-ItemProperty -Path "IIS:\AppPools\$AppPoolName" -Name "processModel.loadUserProfile" -Value $True
}

# 5. Website erstellen
if (Test-Path "IIS:\Sites\$SiteName") {
    Write-Host "Website $SiteName existiert bereits, entferne alte Bindungen..."
    Remove-WebSite -Name $SiteName
}

Write-Host "Erstelle Website $SiteName auf Port $Port..."
New-WebSite -Name $SiteName -Port $Port -PhysicalPath $DestPath -ApplicationPool $AppPoolName

# Windows-Authentifizierung erzwingen (Zwingend für EZKPM Seamless SSO)
Write-Host "Konfiguriere Windows-Authentifizierung für $SiteName..."
Set-WebConfigurationProperty -Filter "/system.webServer/security/authentication/anonymousAuthentication" -Name "enabled" -Value $false -PSPath "IIS:\Sites\$SiteName"
Set-WebConfigurationProperty -Filter "/system.webServer/security/authentication/windowsAuthentication" -Name "enabled" -Value $true -PSPath "IIS:\Sites\$SiteName"

# 6. Berechtigungen setzen (WICHTIG für SQLite!)
Write-Host "Setze Schreibrechte für SQLite-Datenbank..."
$Acl = Get-Acl $DestPath
# IIS_IUSRS vollen Zugriff auf das Verzeichnis geben (für .db, .db-shm, .db-wal)
$AccessRule = New-Object System.Security.AccessControl.FileSystemAccessRule("IIS_IUSRS", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
$Acl.SetAccessRule($AccessRule)
Set-Acl $DestPath $Acl

Write-Host "Deployment erfolgreich abgeschlossen! Der Server ist nun unter http://localhost:$Port erreichbar." -ForegroundColor Green
Write-Host "Öffnen Sie diese URL im Browser, um die Landingpage und den Download zu testen." -ForegroundColor Yellow
