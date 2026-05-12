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

FA 13: Seamless Enterprise SSO & Hardware-Bindung:
In einer Windows-AD-Umgebung erfolgt der reguläre App-Start nahtlos und ohne Passwort-Eingabe (Zero-Touch-Login). Der Master-Key wird durch das Betriebssystem (DPAPI / Windows Hello) geschützt und transparent entschlüsselt.

FA 14: Step-Up Authentifizierung & UI-Session-Timer:
Die Desktop-Applikation unterscheidet strikt zwischen App-Start und Zugriff auf sensible Daten (IsLocked-State).
- Autostart-Regel: Startet die App innerhalb von 5 Minuten nach der Windows-Anmeldung des Nutzers, startet sie im Hintergrund, erzwingt jedoch für sensible UI-Aktionen sofort den Windows-Credential-Prompt. Startet die App nach > 5 Minuten, wird der Start komplett blockiert, bis der Nutzer sich per Windows-Prompt authentifiziert.
- Laufzeit-Sperren: Nach 10 Minuten Inaktivität, bei Minimierung in die Taskleiste für > 1 Minute oder bei sofortiger Minimierung in den System-Tray wird die "Sensitive Session" gesperrt. Für den lesenden Zugriff auf Passwörter im Klartext, Änderungen oder Exporte muss sich der Nutzer dann erneut via Windows-Credentials authentifizieren. 
Wichtig: Standard-Aktivitäten im Hintergrund (z. B. Autofill über die Browser-Extension via Native Messaging) laufen komplett außerhalb dieses Timers und sind jederzeit nahtlos nutzbar, solange die Windows-Sitzung des Nutzers nicht gesperrt ist.

FA 15: Multi-Device Roaming (Hybrid-Fallback):
Wenn der Nutzer an einem komplett neuen, noch nicht gekoppelten Gerät arbeitet:
- Weg A (Geräte-Kopplung): Ein bereits autorisiertes Gerät (z. B. Handy/Alt-PC) genehmigt das neue Gerät durch asymmetrisches Key-Wrapping.
- Weg B (Fallback): Ein 34-stelliger Setup-Code (1Password-Prinzip), der bei vollständigem Geräteverlust am neuen PC manuell eingegeben wird. Danach greift wieder FA 13 (Nahtloses SSO via lokaler OS-Sicherung).

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
 
5.5 Lokalisierung & Sprache (i18n)
 
Code & Fallback: Die gesamte Code-Basis, Variablen, Kommentare sowie die Fallback-Sprache sind zwingend in Englischer Sprache zu verfassen.
Benutzeroberfläche (GUI): Sämtliche Texte, Meldungen und UI-Elemente, mit denen normale Nutzer in Berührung kommen, müssen dynamisch in die Sprache des Clients übersetzt werden.
Übersetzungslogik: Als Grundlage für die Lokalisierung und Übersetzungslogik wird das ResX-System (verwaltet z. B. über ResXManager) verwendet. Hardcodierte Strings in der GUI sind nicht zulässig.

5.6 Corporate Design & Look-and-Feel (Neu):
Ein einheitliches Look & Feel ist bei sicherheitsrelevanter Software das A und O für das Vertrauen der Nutzer. Es sind immer streng die schon vorhandenen Logos und Icons für die Applikation zu verwenden (Sicherung des Wiedererkennungsmerkmals).

6. Projektstatus & Meilensteine

6.1 Abgeschlossene Meilensteine (Erreicht)

✅ Native Messaging Bridge: Chrome/Edge Extension kommuniziert über stdio mit C# Avalonia. BFCache-Bug (Back/Forward Cache) ist durch asynchrone Callbacks behoben.

✅ Avalonia Headless & Kill-Switch: Die App startet unsichtbar. Ein FileSystemWatcher lauscht auf ezkpm_build.lock und beendet Hintergrund-Prozesse sofort beim Kompilieren, um "File in Use"-Fehler zu verhindern.

✅ Enterprise DevEx (i18n): Dynamischer Runtime-Localizer (Localizer.cs) und TranslationSyncService synchronisieren XAML-Keys zur Laufzeit im #if DEBUG Modus mit der AppStrings.resx (keine starken x:Static Bindings).

✅ Audit-Dialog UI (FA 21/22): Orchestrator blockiert Payment-Assets und öffnet ein Topmost-Avalonia-Fenster zur Eingabe von Betrag/Bestellnummer. XAML-Fixes (z.B. AvaloniaXamlLoader.Load(this)) sind aktiv.

✅ Echte Zero-Knowledge Entschlüsselung & Krypto-Roundtrip: Der VaultCryptoService ist vollständig integriert. AesGcm für Asset-Keys und HybridPqcKeyWrapper für asymmetrische AD-Key-Verpackung sind aktiv.

✅ Hardware-gebundener Login (FA 13) & Step-Up Auth (FA 14): DPAPI-Bindung realisiert. Windows Credential Prompt (credui.dll) in Kombination mit 5-Min-Autostart-Logik und Session-Timern voll funktionsfähig.

✅ AD & ACL Rollen (FA 11 & FA 12): AD-Gruppen-Abfragen und SID-basiertes Rechtesystem (Read, Owner, Execute-Only) sind im UI vollständig abgebildet und funktional.

✅ Kryptografisches Audit-Log (FA 4.2 & FA 22): Eingaben (Zahlungsdaten/Sonderaktionen) werden lokal verschlüsselt und per Hash-Chaining verkettet an den Server gesendet (VaultApiClient & VaultCryptoService).

✅ API-Client & Server-Kommunikation: Der VaultApiClient wurde implementiert und ruft verschlüsselte Blobs über den VaultController (PDP) via HTTPS/OIDC ab.

6.2 Offene Anforderungen (Nächste Schritte)

⏳ Multi-Device Roaming (FA 15 - Weg B): Implementierung des 34-stelligen "Secret Key" (1Password-Prinzip) für die plattformunabhängige Geräte-Kopplung und Wiederherstellung bei vollständigem Geräteverlust.

7. Abnahmekriterien

Zero-Knowledge-Proof: Ein Dump der Datenbank ermöglicht keine Rekonstruktion eines einzigen Passworts oder Logs.

AD-Compliance: Berechtigungsänderungen im AD werden sofort vom PDP übernommen.

Rotation-Proof: Ein Asset, das älter als 365 Tage ist, verweigert den Zugriff für Payer-Nutzer.

FIDO-Force: Ohne den registrierten Hardware-Token ist kein Zugriff auf das Schlüsselmaterial möglich.