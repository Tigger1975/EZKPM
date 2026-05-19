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

Write-Host "`n[1/7] Erstelle Lock-Files und stoppe laufenden lokalen Desktop-Client..." -ForegroundColor Yellow
if (!(Test-Path $PublishClientPath)) { New-Item -ItemType Directory -Force -Path $PublishClientPath | Out-Null }
New-Item -ItemType File -Force -Path "$PublishClientPath\ezkpm_build.lock" | Out-Null
Stop-Process -Name "EZKPM.Client.Desktop" -Force -ErrorAction SilentlyContinue

$BuildDate = Get-Date -Format "yyyyMMddHHmmss"

Write-Host "`n[2/7] Kompiliere Server (PDP)..." -ForegroundColor Yellow
if (Test-Path $PublishServerPath) { Remove-Item -Path "$PublishServerPath\*" -Recurse -Force -ErrorAction SilentlyContinue }
dotnet publish "$RepoPath\EZKPM.Server.PDP\EZKPM.Server.PDP.csproj" -c Release -o $PublishServerPath -p:BuildDate=$BuildDate

Write-Host "      Kopiere Wiki/Help System in das Release-Verzeichnis..." -ForegroundColor Yellow
if (!(Test-Path "$PublishServerPath\docs")) { New-Item -ItemType Directory -Force -Path "$PublishServerPath\docs" | Out-Null }
Copy-Item -Path "$RepoPath\docs\wiki" -Destination "$PublishServerPath\docs\wiki" -Recurse -Force

Write-Host "`n[3/7] Kompiliere Client (Desktop & Extension Bridge)..." -ForegroundColor Yellow
if (Test-Path $PublishClientPath) { Remove-Item -Path "$PublishClientPath\*" -Exclude "ezkpm_build.lock" -Recurse -Force -ErrorAction SilentlyContinue }
dotnet publish "$RepoPath\EZKPM.Client.Desktop\EZKPM.Client.Desktop.csproj" -c Release -o $PublishClientPath -p:BuildDate=$BuildDate

$ExtensionDir = "$RepoPath\EZKPM.BrowserExtension"
$ExtensionPem = "$RepoPath\EZKPM.BrowserExtension.pem"
$ExtensionCrx = "$RepoPath\EZKPM.BrowserExtension.crx"

Write-Host "`n[3b/7] Pruefe Browser-Extension auf Aenderungen..." -ForegroundColor Yellow
$needsCrx = $true
if (Test-Path $ExtensionCrx) {
    $crxDate = (Get-Item $ExtensionCrx).LastWriteTime
    $newerExtFiles = Get-ChildItem -Path $ExtensionDir -Recurse | Where-Object { $_.LastWriteTime -gt $crxDate }
    if (-not $newerExtFiles) {
        $needsCrx = $false
    }
}

if ($needsCrx) {
    Write-Host "      Aenderungen erkannt, packe EZKPM.BrowserExtension.crx..." -ForegroundColor Yellow
    if (Test-Path $ExtensionCrx) { Remove-Item -Force $ExtensionCrx }
    
    $edgePath = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
    $chromePath = "${env:ProgramFiles}\Google\Chrome\Application\chrome.exe"
    
    $browserPath = $null
    if (Test-Path $edgePath) { $browserPath = $edgePath }
    elseif (Test-Path $chromePath) { $browserPath = $chromePath }
    
    if ($browserPath) {
        Start-Process -FilePath $browserPath -ArgumentList "--pack-extension=`"$ExtensionDir`"", "--pack-extension-key=`"$ExtensionPem`"" -Wait -NoNewWindow -PassThru | Out-Null
        if (Test-Path $ExtensionCrx) {
            Write-Host "      Erfolgreich gepackt: $ExtensionCrx" -ForegroundColor Green
        } else {
            Write-Host "      Fehler beim Packen der Extension!" -ForegroundColor Red
        }
    } else {
        Write-Host "      Kein Edge oder Chrome gefunden, ueberspringe packen." -ForegroundColor Red
    }
} else {
    Write-Host "      Keine Aenderungen in der Browser-Extension, ueberspringe packen." -ForegroundColor Gray
}

if (!(Test-Path $UpdatesDir)) { New-Item -ItemType Directory -Force -Path $UpdatesDir | Out-Null }
$zipPath = "$UpdatesDir\ClientUpdate.zip"

# Lese InformationalVersion (die den BuildDate beinhaltet)
$clientVersion = dotnet msbuild "$RepoPath\EZKPM.Client.Desktop\EZKPM.Client.Desktop.csproj" -getProperty:InformationalVersion -p:BuildDate=$BuildDate
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
    
    # Clean up debugging symbols to drastically reduce size (saves > 100MB)
    Get-ChildItem -Path $PublishClientPath -Filter *.pdb -Recurse | Remove-Item -Force
    
    # Remove lock file so it doesn't get packaged into the update zip
    Remove-Item -Path "$PublishClientPath\ezkpm_build.lock" -Force -ErrorAction SilentlyContinue
    
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
    if (Test-Path $UpdatesDir) {
        if (!(Test-Path "$IISPath\Updates")) { New-Item -ItemType Directory -Force -Path "$IISPath\Updates" | Out-Null }
        Copy-Item -Path "$UpdatesDir\*" -Destination "$IISPath\Updates" -Force
    }
} catch {
    # Ignore powershell throwing on robocopy exit codes
} finally {
    Write-Host "`n[6/7] Starte IIS durch Entfernen von app_offline.htm..." -ForegroundColor Yellow
    Remove-Item -Path "$IISPath\app_offline.htm" -Force -ErrorAction SilentlyContinue
}

Write-Host "`n[7/8] Verteile Client-Dateien an Netzlaufwerk/Freigabe..." -ForegroundColor Yellow
Write-Host "      Kopiere Client nach T:\Kh\EZKPM_Client\ (wartet bei gesperrten Dateien)..." -ForegroundColor Yellow
if (!(Test-Path "T:\Kh\EZKPM_Client")) { New-Item -ItemType Directory -Force -Path "T:\Kh\EZKPM_Client" | Out-Null }

# Re-create lock files to instantly kill any Native Messaging hosts running from the network share
New-Item -ItemType File -Force -Path "$PublishClientPath\ezkpm_build.lock" | Out-Null
New-Item -ItemType File -Force -Path "T:\Kh\EZKPM_Client\ezkpm_build.lock" | Out-Null
Start-Sleep -Seconds 2

try {
    # /R:1000 = Retry up to 1000 times (approx. 83 minutes)
    # /W:5 = Wait 5 seconds between retries
    # /XD * = Ignore no directories, mirror completely
    robocopy $PublishClientPath "T:\Kh\EZKPM_Client" /MIR /R:1000 /W:5 | Out-Null
} catch {
} finally {
    Remove-Item -Path "$PublishClientPath\ezkpm_build.lock" -Force -ErrorAction SilentlyContinue
    Remove-Item -Path "T:\Kh\EZKPM_Client\ezkpm_build.lock" -Force -ErrorAction SilentlyContinue
}

#Write-Host "`n[8/8] Starte lokalen Desktop-Client..." -ForegroundColor Yellow
#Start-Process "$PublishClientPath\EZKPM.Client.Desktop.exe" -WorkingDirectory $PublishClientPath

Write-Host "`n==========================================" -ForegroundColor Green
Write-Host " Deployment erfolgreich abgeschlossen! " -ForegroundColor Green
Write-Host " Version ausgerollt: $clientVersion" -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green
