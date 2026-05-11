# Run this script AS ADMINISTRATOR
$ErrorActionPreference = "Stop"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "  EZK-PM Enterprise Deployment Pipeline  " -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

# Administrator-Rechte sind nicht mehr zwingend erforderlich, da wir app_offline.htm nutzen.

$RepoPath = "C:\Users\adm-kh\source\repos\EZKPM"
$PublishServerPath = "$RepoPath\Publish\Server"
$PublishClientPath = "$RepoPath\Publish\Client"
$IISPath = "C:\inetpub\EZKPM"
$UpdatesDir = "$PublishServerPath\Updates"

Write-Host "`n[1/7] Stoppe laufenden lokalen Desktop-Client..." -ForegroundColor Yellow
Stop-Process -Name "EZKPM.Client.Desktop" -Force -ErrorAction SilentlyContinue

Write-Host "`n[2/7] Kompiliere Server (PDP)..." -ForegroundColor Yellow
# Kein Remove-Item, damit dotnet publish inkrementell schneller arbeiten kann
dotnet publish "$RepoPath\EZKPM.Server.PDP\EZKPM.Server.PDP.csproj" -c Release -o $PublishServerPath

Write-Host "`n[3/7] Kompiliere Client (Desktop & Extension Bridge)..." -ForegroundColor Yellow
# Kein Remove-Item, um nur geänderte Dateien zu aktualisieren
dotnet publish "$RepoPath\EZKPM.Client.Desktop\EZKPM.Client.Desktop.csproj" -c Release -o $PublishClientPath

if (!(Test-Path $UpdatesDir)) { New-Item -ItemType Directory -Force -Path $UpdatesDir | Out-Null }
$zipPath = "$UpdatesDir\ClientUpdate.zip"

# Lese Version aus csproj aus (für Logs und JSON)
$csproj = [xml](Get-Content "$RepoPath\EZKPM.Client.Desktop\EZKPM.Client.Desktop.csproj")
$clientVersion = $csproj.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($clientVersion)) { $clientVersion = "1.0.0.0" }

$needsZip = $true
if (Test-Path $zipPath) {
    $zipDate = (Get-Item $zipPath).LastWriteTime
    $newerFiles = Get-ChildItem -Path $PublishClientPath -Recurse | Where-Object { $_.LastWriteTime -gt $zipDate }
    if (-not $newerFiles) {
        $needsZip = $false
    }
}

if ($needsZip) {
    Write-Host "`n[4/7] Erstelle OTA-Update Paket fuer Clients (Aenderungen erkannt)..." -ForegroundColor Yellow
    if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
    Compress-Archive -Path "$PublishClientPath\*" -DestinationPath $zipPath -Force

    $versionJson = @{
        LatestVersion = $clientVersion
        ReleaseNotes = "Automatisiertes Deployment (Version $clientVersion)"
        DownloadUrl = "/api/updater/download"
    } | ConvertTo-Json
    Set-Content -Path "$UpdatesDir\version.json" -Value $versionJson
} else {
    Write-Host "`n[4/7] Ueberspringe OTA-Update Paket (Keine Aenderungen im Client)..." -ForegroundColor Gray
}

Write-Host "`n[5/7] Verteile Dateien an den IIS-Produktionsserver..." -ForegroundColor Yellow
Write-Host "      Setze IIS via app_offline.htm in den Wartungsmodus, um Dateisperren zu loesen..." -ForegroundColor Yellow
if (!(Test-Path $IISPath)) { New-Item -ItemType Directory -Force -Path $IISPath | Out-Null }
Set-Content -Path "$IISPath\app_offline.htm" -Value "<html><head><title>Update läuft</title></head><body style='font-family:sans-serif; text-align:center; padding:50px;'><h1>EZKPM wird aktualisiert...</h1><p>Bitte haben Sie ein paar Sekunden Geduld.</p></body></html>"
Start-Sleep -Seconds 3

Write-Host "      Kopiere aktualisierte Dateien (ueberschreibt nur was sich geaendert hat)..." -ForegroundColor Yellow
try {
    robocopy $PublishServerPath $IISPath /MIR /XD Updates /XF app_offline.htm *.db *.db-shm *.db-wal /R:1 /W:1 | Out-Null
} catch {
    # Ignore powershell throwing on robocopy exit codes
} finally {
    Write-Host "`n[6/7] Starte IIS durch Entfernen von app_offline.htm..." -ForegroundColor Yellow
    Remove-Item -Path "$IISPath\app_offline.htm" -Force -ErrorAction SilentlyContinue
}

Write-Host "`n[7/7] Starte lokalen Desktop-Client..." -ForegroundColor Yellow
Start-Process "$PublishClientPath\EZKPM.Client.Desktop.exe"

Write-Host "`n==========================================" -ForegroundColor Green
Write-Host " Deployment erfolgreich abgeschlossen! " -ForegroundColor Green
Write-Host " Version ausgerollt: $clientVersion" -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green
