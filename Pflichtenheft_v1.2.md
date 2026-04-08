Pflichtenheft: Enterprise Zero-Knowledge Password Manager (EZK-PM)

Version: 1.2 (Konsolidiert inkl. V11 Architektur-Regeln)
Status: In Entwicklung
Lizenz: Open-Source (AGPL-3.0)

1. Zielbestimmung & Kernprinzipien

Entwicklung eines hochsicheren, plattformübergreifenden Passwort-Management-Systems für Unternehmen.

Zero-Knowledge: Der Server (PDP - Policy Decision Point) speichert und sieht niemals Klartext-Passwörter oder Krypto-Schlüssel.

Anti-Forensik: Sensible Daten im RAM des Clients (PEP - Policy Enforcement Point) werden durch Pinning und Wiping vor Memory-Dumps geschützt.

Seamless UX: Autofill in Browsern erfolgt über eine Manifest V3 Extension via Native Messaging.

Integration: Nahtlose Active Directory (AD)-Integration und revisionssicheres Zahlungsdaten-Management.

2. Systemarchitektur (Topologie)

Server (PDP): .NET Core API (PostgreSQL), zuständig für Policy-Checks, OIDC-Auth und Blob-Storage.

Client (PEP): Cross-Plattform (.NET Avalonia). Führt alle Krypto-Operationen aus. Läuft als Headless-Hintergrundprozess.

Browser-Extension: Manifest V3, Kommunikation via Native Messaging mit dem Desktop-Client für Autofill und Stealth-Injection.

3. Funktionale Anforderungen (FA)

3.1 Benutzer- & Zugriffsverwaltung

FA 10: Authentisierung via OIDC (SSO) gegen Active Directory.

FA 11: Rollenbasiertes Rechtesystem (RBAC) auf Basis von AD-SIDs.

FA 12: Unterstützung von Execute-Only (Benutzen ohne Einsehen), Read und Owner Berechtigungen.

FA 13: Hardware-gebundener Login via FIDO2 (hmac-secret) oder lokalem TPM zur Ableitung des Master-Keys.

3.2 Tresor-Funktionen (Vault Assets)

FA 20: Speicherung von Passwörtern, Passkeys, TOTP-Seeds und RSA-Keys (SSH-Agent-Mode).

FA 21: Payment-Assets: Unterstützung für Kreditkarten und Zahlungsdienste (z. B. PayPal).

FA 22: Pflicht-Logging: Erzwingung von Dateneingaben (Betrag, Bestellnummer) vor dem Autofill bei Zahlungsdaten.

3.3 Lifecycle & Rotation

FA 30: Maximale Lebenszeit: Credentials verfallen nach 365 Tagen.

FA 31: Rotation-Assistant: Interaktives Tool für Owner zum Ändern von Passwörtern (altes PW + 2x neues PW).

FA 32: Automatisierte Sperrung für Payer bei Ablauf der 365-Tage-Frist.

3.4 Gruppen & Sharing

FA 40: Asymmetrisches Key-Wrapping zum Teilen von Tresoren ohne Server-Kenntnis.

FA 41: Multi-Owner-Prinzip für die Wiederherstellung und Log-Einsicht.

4. Produktdaten (Datenarchitektur)

4.1 Verschlüsselung (Krypto-Kern)

Algorithmen: Symmetrisch (AES-256-GCM), Asymmetrisch (Ed25519), Post-Quantum-Hybrid (X25519 + Kyber/ML-KEM).

Key Derivation: Argon2id für Master-Passwort-Ableitung.

4.2 Datenbank (Server-Seite)

Technologie: PostgreSQL.

Inhalt: Nur Metadaten (Hashed URLs/IDs), ACL-Mappings und verschlüsselte Blobs. Kein Klartext-Bezug zu Diensten.

5. Nichtfunktionale Anforderungen (NFA) / Sicherheit & Architektur

5.1 Zero-Knowledge-Garantie

Der Server oder Administrator darf niemals technisch in der Lage sein, die Passwörter oder Logs der Nutzer zu entschlüsseln.

5.2 Revisionssicherheit (Audit)

Hash-Chaining: Alle Log-Einträge sind kryptografisch miteinander verkettet. Manipulationen/Löschungen durch den Server-Admin führen zum Bruch der Kette und zum Alarm im Client.

5.3 Client-Integrität, Forensik & Stabilität

Remote Attestation: Server liefert Daten nur an verifizierte Client-Binärdateien aus.

Anti-Forensik: Nutzung von Non-Pageable RAM (SecureMemory Klasse mit GCHandle Pinned); automatisches Wiping sensitiver Daten (CryptographicOperations.ZeroMemory).

Cross-Assembly-Binding: Dynamischer Datenaustausch zwischen Projekten (Core <-> Desktop) nutzt zwingend System.Dynamic.ExpandoObject, um RuntimeBinderException zu verhindern.

Native Messaging Strictness: Absolutes Verbot von Console.WriteLine() oder .LogToTrace(). Logging erfolgt ausschließlich in Textdateien (Program.LogDebug()), um den JSON-Stream (stdout) nicht zu korrumpieren.

Documentation-as-Code (Neu): Pflichtenheft und AI-Context-Prompts werden zwingend im Git-Repository unter /docs mitgeführt, um die architektonische Integrität und AI-Alignment über den gesamten Lebenszyklus sicherzustellen.

5.4 Notfall-Mechanismen

Shamir’s Secret Sharing: Wiederherstellung des Master-Zugangs nur durch Zusammenführung mehrerer Admin-Key-Fragmente (Break-Glass).

Duress-Logic: Spezielles Passwort öffnet "Fake-Vault" und setzt stillen Alarm ab.

6. Projektstatus & Meilensteine

6.1 Abgeschlossene Meilensteine (Erreicht)

✅ Native Messaging Bridge: Chrome/Edge Extension kommuniziert über stdio mit C# Avalonia. BFCache-Bug (Back/Forward Cache) ist durch asynchrone Callbacks behoben.

✅ Avalonia Headless & Kill-Switch: Die App startet unsichtbar. Ein FileSystemWatcher lauscht auf ezkpm_build.lock und beendet Hintergrund-Prozesse sofort beim Kompilieren, um "File in Use"-Fehler zu verhindern.

✅ Enterprise DevEx (i18n): Dynamischer Runtime-Localizer (Localizer.cs) und TranslationSyncService synchronisieren XAML-Keys zur Laufzeit im #if DEBUG Modus mit der AppStrings.resx (keine starken x:Static Bindings).

✅ Audit-Dialog UI (FA 21/22): Orchestrator blockiert Payment-Assets und öffnet ein Topmost-Avalonia-Fenster zur Eingabe von Betrag/Bestellnummer. XAML-Fixes (z.B. AvaloniaXamlLoader.Load(this)) sind aktiv.

6.2 Offene Anforderungen (Nächste Schritte)

⏳ API-Client & Server-Kommunikation: Ein VaultApiClient muss implementiert werden, um mit dem VaultController (PDP) via HTTPS/OIDC zu kommunizieren und verschlüsselte Blobs abzurufen.

⏳ Kryptografisches Audit-Log (FA 4.2 & FA 22): Die Eingaben aus dem AuditDialog müssen lokal mit AES-GCM verschlüsselt und mit dem Hash des vorherigen Eintrags verkettet werden (Hash-Chaining), bevor sie an den Server gesendet werden.

⏳ Echte Zero-Knowledge Entschlüsselung: Der VaultCryptoService muss in den Orchestrator integriert werden, um Server-Blobs sicher lokal im RAM zu entschlüsseln (Ersatz der Dummy-Daten).

⏳ Hardware-gebundener Login (FA 13): Ableitung des Master-Keys durch Master-Passwort (Argon2id) + FIDO2/WebAuthn HMAC.

7. Abnahmekriterien

Zero-Knowledge-Proof: Ein Dump der Datenbank ermöglicht keine Rekonstruktion eines einzigen Passworts oder Logs.

AD-Compliance: Berechtigungsänderungen im AD werden sofort vom PDP übernommen.

Rotation-Proof: Ein Asset, das älter als 365 Tage ist, verweigert den Zugriff für Payer-Nutzer.

FIDO-Force: Ohne den registrierten Hardware-Token ist kein Zugriff auf das Schlüsselmaterial möglich.