# AI Agent Code Integrity & Preservation Protocol

**Status:** ACTIVE
**Priority:** CRITICAL
**Enforcement:** MANDATORY FOR ALL AGENT ACTIONS

Dieses Regelwerk wurde explizit entwickelt, um zu verhindern, dass die KI-Agenten bei Code-Anpassungen, Bugfixes oder Refactorings ungewollt bestehenden Code, UI-Elemente, Lokalisierungs-Strings oder Funktionen "über Bord werfen" oder beschädigen. Es dient als Erweiterung des `Pflichtenhefts_v1.2` und greift in jeden Operationszyklus ein.

## 1. Verbot blinder Ersetzungen ("No Blind Replace")
* **Regel:** Der Agent darf Code NIEMALS ändern, ohne vorher die betroffene Datei vollständig oder großflächig eingelesen zu haben (`view_file`).
* **Sicherheitsmaßnahme:** Es dürfen keine massiven `multi_replace_file_content` Aufrufe getätigt werden, bei denen mehr als der absolut notwendige Block ersetzt wird. Bestehende Event-Handler, UI-Borders, XAML-Tags oder Konfigurations-Keys müssen zwingend erhalten bleiben.
* **Prüfung:** Vor jeder Löschung oder Ersetzung muss der Agent prüfen: "Ist dieser Code-Block tatsächlich obsolet oder wird er von einer anderen Funktion (z.B. UI-Design, Hintergrund-Jobs) noch benötigt?"

## 2. Compile-and-Test-Garantie (Zero-Breakage)
* **Regel:** Nach *jeder* Anpassung an `.cs` oder `.axaml` Dateien ist **zwingend** ein `dotnet build` auszuführen.
* **Sicherheitsmaßnahme:** Der Agent MUSS die Warnungen und Fehler aus dem Build überprüfen. Treten neue Warnungen auf, darf nicht blind weitergearbeitet werden, bis diese verstanden oder behoben sind.
* **Skript-Sicherheit:** Wenn PowerShell- oder Deployment-Skripte (z.B. `Deploy-All.ps1`) angepasst werden, muss zwingend über `WorkingDirectory` oder absolute Pfade sichergestellt werden, dass keine Pfad-Abhängigkeiten zerrissen werden.

## 3. UI- und Lokalisierungs-Schutz (i18n & XAML)
* **Regel:** Bestehende XAML-Strukturen, Grid-Layouts, StackPanels und Borders dürfen bei Refactorings (wie z.B. dem Hinzufügen eines Buttons) nicht entfernt oder "vereinfacht" werden.
* **Sicherheitsmaßnahme:** Wenn Hardcoded-Strings zu Lokalisierungs-Keys (`AppStrings`) umgebaut werden (oder umgekehrt), muss die *exakte* Textstruktur und Semantik erhalten bleiben. Die Fallback-Sprache darf nicht das UI der anderen Sprachen zerstören.

## 4. Pflichtenheft-Compliance & Konsens-Prinzip
* **Regel:** Das `Pflichtenheft_v1.2` bildet die absolute Grundlage der Architektur. Kern-Features (z.B. FA 14 Seamless SSO, FA 22 Audit Logs, Native-Messaging-Vorgaben) dürfen nicht eigenmächtig durch Refactoring ausgehebelt werden.
* **Ausnahme & Konsens:** Wenn technische Erfordernisse, Optimierungen oder neue Erkenntnisse dem Pflichtenheft widersprechen, ist das Pflichtenheft NICHT unfehlbar. 
* **Aktion:** In einem solchen Fall stoppt der Agent, weist den Nutzer proaktiv auf den Widerspruch hin und schlägt eine Anpassung vor. **Keine Abweichung vom Pflichtenheft ohne explizite Zustimmung des Nutzers.** Erst wenn ein Konsens erzielt wurde, wird das Pflichtenheft offiziell aktualisiert und der Code entsprechend angepasst.

## 5. Langsame und sichere Iteration ("Do Not Rush")
* **Regel:** Wenn das Problem nicht zu 100 % klar ist, rät der Agent nicht und löscht keine Dateien "auf Verdacht".
* **Sicherheitsmaßnahme:** Der Agent teilt komplexe Refactorings in kleine, prüfbare Teilschritte auf. Er gibt explizit im Chat Bescheid, welche Datei in welchem Umfang geändert wird.

## 6. Sanktion & Verpflichtung
Als KI-Entwickler verpflichte ich mich, dieses `10-Code-Integrity-Protocol.md` VOR JEDEM File-Edit zu berücksichtigen. Die Erhaltung der existierenden Architektur hat höchste Priorität. Jeder Schritt wird doppelt validiert.
