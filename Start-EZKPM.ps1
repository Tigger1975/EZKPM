Write-Host "Beende laufende EZKPM Prozesse..." -ForegroundColor Yellow
#Stop-Process -Name "EZKPM.Server.PDP", "EZKPM.Client.Desktop" -Force -ErrorAction SilentlyContinue
Stop-Process -Name  "EZKPM.Client.Desktop" -Force -ErrorAction SilentlyContinue
# Gib den Ports und Pipes kurz Zeit, sich freizugeben
Start-Sleep -Seconds 1

$baseDir = $PSScriptRoot

$serverProj = Join-Path $baseDir "EZKPM.Server.PDP\EZKPM.Server.PDP.csproj"
$clientProj = Join-Path $baseDir "EZKPM.Client.Desktop\EZKPM.Client.Desktop.csproj"

Write-Host "Starte EZKPM Server..." -ForegroundColor Green
#Start-Process "dotnet" -ArgumentList "run --project `"$serverProj`""

# Kurze Pause, damit der Server hochfahren kann (wichtig für die API)
Start-Sleep -Seconds 3

Write-Host "Starte EZKPM Client..." -ForegroundColor Green
Start-Process "dotnet" -ArgumentList "run --project `"$clientProj`""

Write-Host "Fertig!" -ForegroundColor Cyan
