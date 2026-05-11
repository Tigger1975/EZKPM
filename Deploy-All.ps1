# Run this script AS ADMINISTRATOR
$ErrorActionPreference = "Stop"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "  EZK-PM Enterprise Deployment Pipeline  " -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

# 1. Prüfen ob Admin-Rechte vorliegen
$wid = [System.Security.Principal.WindowsIdentity]::GetCurrent()
$prp = new-object System.Security.Principal.WindowsPrincipal($wid)
if (-not $prp.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "FEHLER: Dieses Skript muss zwingend als Administrator ausgeführt werden!"
    exit
}

$RepoPath = "C:\Users\adm-kh\source\repos\EZKPM"
$PublishServerPath = "$RepoPath\Publish\Server"
$PublishClientPath = "$RepoPath\Publish\Client"
$IISPath = "C:\inetpub\EZKPM"
$UpdatesDir = "$PublishServerPath\Updates"

Write-Host "`n[1/7] Stoppe laufenden lokalen Desktop-Client..." -ForegroundColor Yellow
Stop-Process -Name "EZKPM.Client.Desktop" -Force -ErrorAction SilentlyContinue

Write-Host "`n[2/7] Kompiliere Server (PDP)..." -ForegroundColor Yellow
if (Test-Path $PublishServerPath) { Remove-Item -Path "$PublishServerPath\*" -Recurse -Force -Exclude "Updates" -ErrorAction SilentlyContinue }
dotnet publish "$RepoPath\EZKPM.Server.PDP\EZKPM.Server.PDP.csproj" -c Release -o $PublishServerPath

Write-Host "`n[3/7] Kompiliere Client (Desktop & Extension Bridge)..." -ForegroundColor Yellow
if (Test-Path $PublishClientPath) { Remove-Item -Path "$PublishClientPath\*" -Recurse -Force -ErrorAction SilentlyContinue }
dotnet publish "$RepoPath\EZKPM.Client.Desktop\EZKPM.Client.Desktop.csproj" -c Release -o $PublishClientPath

Write-Host "`n[4/7] Erstelle OTA-Update Paket für Clients..." -ForegroundColor Yellow
if (!(Test-Path $UpdatesDir)) { New-Item -ItemType Directory -Force -Path $UpdatesDir | Out-Null }
$zipPath = "$UpdatesDir\ClientUpdate.zip"
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
Compress-Archive -Path "$PublishClientPath\*" -DestinationPath $zipPath -Force

# Lese Version aus csproj aus
$csproj = [xml](Get-Content "$RepoPath\EZKPM.Client.Desktop\EZKPM.Client.Desktop.csproj")
$clientVersion = $csproj.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($clientVersion)) { $clientVersion = "1.0.0.0" }

$versionJson = @{
    LatestVersion = $clientVersion
    ReleaseNotes = "Automatisiertes Deployment (Version $clientVersion)"
    DownloadUrl = "/api/updater/download"
} | ConvertTo-Json
Set-Content -Path "$UpdatesDir\version.json" -Value $versionJson

Write-Host "`n[5/7] Verteile Dateien an den IIS-Produktionsserver..." -ForegroundColor Yellow
Write-Host "      Stoppe IIS AppPool, um Dateisperren zu vermeiden..." -ForegroundColor Yellow
Stop-WebAppPool -Name "EZKPM_AppPool" -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

if (!(Test-Path $IISPath)) { New-Item -ItemType Directory -Force -Path $IISPath | Out-Null }
Copy-Item -Path "$PublishServerPath\*" -Destination $IISPath -Recurse -Force

Write-Host "`n[6/7] Starte IIS AppPool..." -ForegroundColor Yellow
Start-WebAppPool -Name "EZKPM_AppPool" -ErrorAction SilentlyContinue

Write-Host "`n[7/7] Starte lokalen Desktop-Client..." -ForegroundColor Yellow
Start-Process "$PublishClientPath\EZKPM.Client.Desktop.exe"

Write-Host "`n==========================================" -ForegroundColor Green
Write-Host " Deployment erfolgreich abgeschlossen! " -ForegroundColor Green
Write-Host " Version ausgerollt: $clientVersion" -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green
