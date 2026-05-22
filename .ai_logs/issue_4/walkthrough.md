Die Funktionalität zum Packen der Browser-Extension (`.crx`) wurde in das Deployment-Skript integriert.

1. **Prüfung auf Änderungen:** Das Skript `Deploy-All.ps1` vergleicht nun das Datum der bestehenden `EZKPM.BrowserExtension.crx` (falls vorhanden) mit den Dateien im Ordner `EZKPM.BrowserExtension`.
2. **Automatisches Packen:** Wird eine Änderung festgestellt, wird automatisch `msedge.exe` (oder als Fallback `chrome.exe`) per Kommandozeile (`--pack-extension`) gestartet.
3. **Signatur:** Der bestehende Private Key (`EZKPM.BrowserExtension.pem`) wird via `--pack-extension-key` übergeben, sodass die Identität der Extension gleich bleibt.
4. **Integration:** Der Schritt passiert als Schritt "3b/7" im Deployment, direkt nachdem der Desktop Client kompiliert wurde und bevor das OTA Update-Zip geschnürt wird.