/**
 * Hintergrund-Service-Worker (Manifest V3).
 * Hält die ständige Verbindung zum sicheren Desktop-Client (PEP) über stdin/stdout.
 */
const NATIVE_APP_NAME = "com.ironcladvault.ezkpm";
let nativePort = null;

function connectToNativeHost() {
    nativePort = chrome.runtime.connectNative(NATIVE_APP_NAME);

    nativePort.onMessage.addListener((message) => {
        console.log("Nachricht vom sicheren Desktop-Client erhalten.");
        // Leite die entschlüsselten Daten oder Audit-Prompts an den Content-Script des aktiven Tabs weiter
        chrome.tabs.query({ active: true, currentWindow: true }, (tabs) => {
            if (tabs[0]) {
                chrome.tabs.sendMessage(tabs[0].id, message);
            }
        });
    });

    nativePort.onDisconnect.addListener(() => {
        console.warn("Verbindung zum Desktop-Client verloren. Läuft EZK-PM?");
        nativePort = null;
        // Reconnect-Logik könnte hier greifen
    });
}

// Initiale Verbindung aufbauen
connectToNativeHost();

// Lausche auf Anfragen vom Content-Script (aus der Webseite)
chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
    if (request.type === "REQUEST_AUTOFILL") {
        if (!nativePort) {
            connectToNativeHost();
        }
        
        // Sende die Anfrage (z.B. URL) über die sichere Pipe an den C# Desktop-Client
        if (nativePort) {
            nativePort.postMessage({
                Type: "REQUEST_AUTOFILL",
                Url: request.url
            });
        }
    }
    return true;
});