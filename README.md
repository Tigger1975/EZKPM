Ironclad Vault (EZK-PM) 🛡️

Enterprise Zero-Knowledge Password Manager

Ironclad Vault (EZK-PM) ist eine hochsichere Open-Source-Lösung für das Enterprise-Passwortmanagement auf Basis von .NET 10. Die Architektur folgt einem strikten Zero-Knowledge-Ansatz, bei dem der Server niemals Zugriff auf Klartextdaten oder kryptografische Schlüssel hat.

🚀 Kernmerkmale (Update .NET 10)

Zero-Knowledge Architektur: Sämtliche Krypto-Operationen erfolgen exklusiv auf dem Client (PEP).

Hardware-Anker (FA 13): Login-Sicherheit durch FIDO2 (hmac-secret) und lokale TPM-Bindung.

Payment-Asset Governance (FA 21/22): Revisionssicheres Logging von Zahlungsdaten.

Anti-Forensik: Nutzung von Non-Pageable RAM (SecureMemory) und aktives RAM-Wiping.

🏗️ System-Architektur

EZKPM.Server.PDP: ASP.NET Core Web-API (.NET 10).

EZKPM.Client.Desktop (PEP): Avalonia-basierte App (.NET 10).

Browser-Extension: Manifest V3 Brücke.

🛠️ Entwickler-Features

MSBuild Kill-Switch: FileSystemWatcher auf ezkpm_build.lock.

Dynamisches i18n: Laufzeit-Lokalisierungssystem für .NET 10 optimiert.

Created by Lead-Developer & Security-Architect - Ironclad Vault Project