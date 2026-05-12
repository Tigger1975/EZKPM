Ironclad Vault (EZK-PM) 🛡️

Enterprise Zero-Knowledge Password Manager

Ironclad Vault (EZK-PM) ist eine hochsichere Open-Source-Lösung für das Enterprise-Passwortmanagement auf Basis von .NET 10. Die Architektur folgt einem strikten Zero-Knowledge-Ansatz, bei dem der Server niemals Zugriff auf Klartextdaten oder kryptografische Schlüssel hat.

🚀 Kernmerkmale (Update .NET 10)

1. **Zero-Knowledge Architektur:** Sämtliche Krypto-Operationen erfolgen exklusiv auf dem Client (PEP).
2. **Hardware-Anker (FA 13):** Login-Sicherheit durch FIDO2 (hmac-secret) und lokale TPM-Bindung.
3. **Payment-Asset Governance (FA 21/22):** Revisionssicheres Logging von Zahlungsdaten.
4. **Anti-Forensik:** Nutzung von Non-Pageable RAM (SecureMemory) und aktives RAM-Wiping.

## 🏗️ System-Architektur

* **EZKPM.Server.PDP:** ASP.NET Core Web-API (.NET 10). Agiert als "Blind Data Store" (Speichert nur Hashes und Cipher-Blobs).
* **EZKPM.Client.Desktop (PEP):** Avalonia-basierte App (.NET 10). Übernimmt sämtliche Entschlüsselung, UI und AD-Integration.
* **Browser-Extension:** Manifest V3 Brücke via Native Messaging für Zero-Knowledge-Autofill im Browser.

## ⚙️ Administration & Automatisierung (Headless API)

Der Client verfügt über einen integrierten **Local Admin API Host** (via `HttpListener`), der die Steuerung über externe Skripte (z. B. PowerShell, Cron) für Administratoren ermöglicht.

### Start-Parameter (CLI)
Der Desktop-Client kann mit speziellen Parametern gestartet werden:
* `--headless`
  Startet den Client im Hintergrund-Modus (ohne UI). Entschlüsselt den Vault automatisch über die in Windows gespeicherten DPAPI-Credentials und startet die Local Admin API.
* `--minimize`
  Startet den Client minimiert im System-Tray.

### Konfiguration (`config.json`)
Folgende Felder können in der `config.json` des Clients für die Admin-API konfiguriert werden:
* `LocalApiPort`: Port für den lokalen API Host (Standard: `5050`).
* `LocalApiAllowedSid`: SID des berechtigten Admin- bzw. Dienstekontos. Nur Anfragen von dieser Windows-Identität (Negotiate/NTLM) werden vom Client akzeptiert.

### Verfügbare Local Admin API Endpunkte
* `GET http://localhost:5050/api/admin/ping`
  Status-Check der API.
* `POST http://localhost:5050/api/admin/invite` (Body: `{ "Sid": "...", "SamAccountName": "..." }`)
  Löst lokal das Hashing der Identitätsdaten aus und triggert die Einladung eines neuen Users beim PDP-Server.
* `POST http://localhost:5050/api/admin/sync`
  Zwingt den Client, sofort die lokalen Active-Directory-Mitgliedschaften abzufragen und die ACL-Berechtigungen (Owner, Read, Execute) für alle verwalteten Assets abzugleichen.

## 👥 Admin-Dashboard (GUI)
Im UI-Modus bietet der Client ein **Admin Dashboard** mit folgenden Funktionen:
* **AD-Integration:** Suchen und Ernennen von Administratoren direkt aus dem Active Directory.
* **Benutzerverwaltung:** Übersicht aller (existierenden und eingeladenen) Benutzer, kombiniert mit dem Live-Status aus dem AD (Registriert, Eingeladen, Deaktiviert) sowie dem "Zuletzt online"-Zeitstempel.
* **Einladungen:** Komfortables Erzeugen von Pairing-Codes für die Onboarding-Email.

## 🛠️ Entwickler-Features

* **MSBuild Kill-Switch:** FileSystemWatcher auf `ezkpm_build.lock`.
* **Dynamisches i18n:** Laufzeit-Lokalisierungssystem für .NET 10 optimiert.

---
*Created by Lead-Developer & Security-Architect - Ironclad Vault Project*