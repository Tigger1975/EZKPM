Import-Module WebAdministration
Set-ItemProperty -Path "IIS:\AppPools\EZKPM_AppPool" -Name "processModel.loadUserProfile" -Value $True
Restart-WebAppPool "EZKPM_AppPool"
Write-Host "AppPool 'EZKPM_AppPool' loadUserProfile = True gesetzt und neu gestartet." -ForegroundColor Green
