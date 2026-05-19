### Implementierungsplan für die Browser-Extension Deployment-Erweiterung

**Ziel:** Automatisiertes Erstellen der `EZKPM.BrowserExtension.crx`, sobald Quellcode-Änderungen im Extension-Verzeichnis erkannt werden.

**Vorgehen:**
1. **Anpassung der `Deploy-All.ps1`:**
   - Einbau eines neuen Deployment-Schritts vor oder nach dem Client-Update-Check.
   - Definieren der Pfade: 
     - Extension-Ordner: `$RepoPath\EZKPM.BrowserExtension`
     - Private Key: `$RepoPath\EZKPM.BrowserExtension.pem`
     - Ziel-CRX: `$RepoPath\EZKPM.BrowserExtension.crx`
   - **Prüfung auf Änderungen:** Vergleich des `LastWriteTime` der `.crx`-Datei mit allen Dateien im `EZKPM.BrowserExtension`-Ordner.
   - **Packen der Extension:** Wenn Änderungen vorliegen (oder keine `.crx` existiert), Aufruf von Chrome oder Microsoft Edge über die Kommandozeile (`msedge.exe` oder `chrome.exe`) mit den Parametern `--pack-extension` und `--pack-extension-key`. Dies erzeugt automatisch die Datei `EZKPM.BrowserExtension.crx`.

Damit ist gewährleistet, dass bei jeder Änderung an der Extension (z.B. neue Scripts/Styles) sofort eine signierte, verteilfertige `.crx` zur Verfügung steht.