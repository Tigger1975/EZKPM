# Administrator Guide

This section is dedicated to IT Administrators managing the EZKPM infrastructure.

## 1. Accessing the Admin Dashboard
The Admin Dashboard is only visible to users whose Active Directory SID is registered as a Global Administrator on the EZKPM Server. If you are an admin, you will see a prominent **"🛡️ Admin & Recovery"** button in the left sidebar.

![Admin Dashboard](images/admin_dashboard.png)

## 2. Active Directory Integration & RBAC

EZKPM does not maintain its own user list. Everything is tied to the central Active Directory (AD). 
- **Roles:** Permissions are assigned to Folders by mapping them to AD Group SIDs.
- **Role Types:**
  - `Owner`: Can read, edit, delete, and rotate passwords.
  - `Read`: Can view and use passwords.
  - `Execute-Only`: (Enterprise feature) Can use the password via the Browser Extension for Autofill, but cannot view the plaintext password in the UI or copy it to the clipboard.

## 3. Separation of Duties (Identity Mapping)
For highly privileged IT personnel, it is common to have a standard account (e.g., `kh`) and an admin account (e.g., `adm-kh`).
In the Admin Dashboard under **"Identitäten (Mapping)"**, you can link these two AD accounts together. This ensures that the system recognizes both accounts as the *same physical person*. This is crucial for enforcing the **6-Eyes-Principle** (Separation of Duties), preventing an admin from approving their own recovery request using their secondary account.

## 4. Benutzerverwaltung & Einladungen
Im Tab **"Benutzerverwaltung"** können Sie eine vollständige Liste aller registrierten und eingeladenen Benutzer einsehen. Diese Liste gleicht die Server-Daten lokal mit dem Active Directory ab und zeigt an, wer registriert ist, wessen Einladung noch aussteht, und welcher Account im AD deaktiviert wurde. Ebenfalls sehen Sie dort, wann sich der Nutzer zuletzt erfolgreich mit dem Server verbunden hat (`Zuletzt online`).

Unter **"Benutzer Einladen"** können Sie gezielt nach AD-Benutzern suchen und den Pairing-Prozess anstoßen. Der Client kommuniziert mit dem Server, generiert den kryptografischen Pairing-Code und bereitet den E-Mail-Versand vor.

## 5. Automatisierung & Headless Admin API
Der Desktop-Client verfügt über eine integrierte REST-API (Local Admin API Host via `HttpListener`), um EZKPM durch externe Skripte (z. B. PowerShell, Cron) fernzusteuern. Dies ist besonders nützlich für automatisierte Onboarding- oder Offboarding-Prozesse (z. B. wenn das Identity Management-System der HR-Abteilung einen neuen Nutzer anlegt).

### 5.1. Start-Parameter (CLI)
Um den Client per Skript oder Autostart zu steuern, können Sie folgende Command-Line-Parameter verwenden:
* `EZKPM.Client.Desktop.exe --headless`
  Startet den Client vollständig im Hintergrund (keine UI). Der lokale Vault wird automatisch über die DPAPI (Windows) entschlüsselt. Startet den Local Admin API Host.
* `EZKPM.Client.Desktop.exe --minimize`
  Startet den Client normal, minimiert ihn aber direkt ins System-Tray.

### 5.2. API-Konfiguration (`config.json`)
Passen Sie die `config.json` im App-Data-Verzeichnis an, um die API abzusichern:
* `LocalApiPort`: Der Port des HTTP-Listeners (Standard: `5050`).
* `LocalApiAllowedSid`: Die Windows-SID des berechtigten Dienstekontos (z. B. `Cron.xyz`). **Sicherheit:** Die API erzwingt NTLM/Negotiate-Authentifizierung und blockiert alle Aufrufe, deren Aufrufer-SID nicht mit dieser Einstellung übereinstimmt.

### 5.3. Endpunkte der Local Admin API
* `GET http://localhost:5050/api/admin/ping`
  Status-Check, ob die API erreichbar ist.
* `POST http://localhost:5050/api/admin/invite` (Body: `{ "Sid": "S-1-5-21-...", "SamAccountName": "mustermann" }`)
  Löst den lokalen Zero-Knowledge-Hashing-Vorgang aus und fordert beim Server einen neuen Pairing-Code für den Nutzer an.
* `POST http://localhost:5050/api/admin/sync`
  Zwingt den laufenden Client, die lokalen Active-Directory-Gruppenmitgliedschaften neu aufzulösen. Dabei werden die Owner/Read/Execute-Berechtigungen lokal aktualisiert, ohne den Benutzer zu löschen, falls er eine Gruppe verlässt (wodurch die Assets konsistent bleiben).

## 6. Security Alerts & Vulnerability Scans
The **"Sicherheitswarnungen"** tab aggregates CVEs (Common Vulnerabilities and Exposures) detected in software packages where you store credentials. For example, if a stored Server Asset uses a specific version of a web service that becomes compromised, EZKPM will alert the administrators here.

## 7. Audit Logs & Forensic Decryption
As mandated by **FA 4.2 / FA 22**, the system maintains a kryptografically hash-chained Audit Log.
Under **"System Keys & Logs"**, Admins possessing the `Environment Log Key` can download encrypted logs from the server and decrypt them locally to trace usage (e.g., finding out who accessed a payment card and what justification they provided).
