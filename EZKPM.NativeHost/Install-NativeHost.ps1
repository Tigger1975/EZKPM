$manifestName = "com.ezkpm.nativehost"
$exePath = Join-Path $PSScriptRoot "bin\Debug\net10.0\EZKPM.NativeHost.exe"
$manifestPath = Join-Path $PSScriptRoot "com.ezkpm.nativehost.json"

# Create the JSON manifest
$manifest = @"
{
  "name": "$manifestName",
  "description": "EZKPM Native Messaging Host",
  "path": "$($exePath.Replace('\', '\\'))",
  "type": "stdio",
  "allowed_origins": [
    "chrome-extension://ofiilabemldhhdggobjdbdfelbmpmklf/"
  ]
}
"@

Set-Content -Path $manifestPath -Value $manifest -Encoding UTF8

# Register for Chrome
$chromeKey = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$manifestName"
if (-not (Test-Path $chromeKey)) {
    New-Item -Path $chromeKey -Force | Out-Null
}
Set-ItemProperty -Path $chromeKey -Name "(default)" -Value $manifestPath

# Register for Edge
$edgeKey = "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\$manifestName"
if (-not (Test-Path $edgeKey)) {
    New-Item -Path $edgeKey -Force | Out-Null
}
Set-ItemProperty -Path $edgeKey -Name "(default)" -Value $manifestPath

Write-Host "Native Messaging Host registered successfully!"
Write-Host "Manifest Path: $manifestPath"
Write-Host "Executable Path: $exePath"
