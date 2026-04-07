/**
 * Stealth Injection Script (Content Script).
 * Läuft im Kontext der Zielwebseite, isoliert von der Haupt-JS-Umgebung der Seite.
 */

// Suchen nach Login- oder Payment-Formularen
function scanForForms() {
    const passwordInputs = document.querySelectorAll('input[type="password"]');
    const ccInputs = document.querySelectorAll('input[name*="card"], input[id*="cc"]'); // Heuristik für Payment-Assets (FA 21)

    if (passwordInputs.length > 0 || ccInputs.length > 0) {
        // Formular gefunden, wir fragen den Background-Worker (und damit den Desktop-Client), 
        // ob wir Credentials für diese URL haben.
        chrome.runtime.sendMessage({ 
            type: "REQUEST_AUTOFILL", 
            url: window.location.hostname 
        });
    }
}

// Antwort vom Background-Worker (bzw. dem nativen C# Client) verarbeiten
chrome.runtime.onMessage.addListener((message) => {
    if (message.Type === "AUDIT_REQUIRED") {
        // FA 22 (Pflicht-Logging): Der Desktop-Client blockiert die Herausgabe.
        // Wir blenden einen Hinweis auf der Webseite ein, dass der Nutzer in die Desktop-App wechseln muss.
        alert("EZK-PM Security: " + message.Message);
    } 
    else if (message.Type === "AUTOFILL_DATA") {
        // Stealth-Injection der Daten
        performStealthInjection(message.Username, message.Password);
        
        // ANTI-FORENSIK (FA 4.3): Variablen im JS-Speicher sofort nullen, 
        // damit kein Web-Skript sie aus dem RAM kratzen kann.
        message.Username = null;
        message.Password = null;
        message = null;
    }
});

function performStealthInjection(username, password) {
    const userField = document.querySelector('input[type="text"], input[type="email"]');
    const passField = document.querySelector('input[type="password"]');

    if (userField && passField) {
        userField.value = username;
        passField.value = password;
        
        // Events feuern, damit SPA-Frameworks (React, Angular) die Änderung bemerken
        userField.dispatchEvent(new Event('input', { bubbles: true }));
        passField.dispatchEvent(new Event('input', { bubbles: true }));
    }
}

// Initiale Ausführung
setTimeout(scanForForms, 1000);