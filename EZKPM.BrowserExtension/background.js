/**
 * Hintergrund-Service-Worker (Manifest V3).
 * Hält die ständige Verbindung zum sicheren Desktop-Client (PEP) über stdin/stdout.
 */
const NATIVE_APP_NAME = "com.ezkpm.nativehost";
let nativePort = null;
let reconnectTimer = null;

function setExtensionIcon(state) {
    let path = "icons/icon_gray.png";
    let title = "EZKPM: Disconnected";
    if (state === 'connected') {
        path = "icons/icon_green.png";
        title = "EZKPM: Connected";
    }
    if (state === 'error') {
        path = "icons/icon_red.png";
        title = "EZKPM: Error / Lost Connection";
    }
    
    try {
        chrome.action.setIcon({ path: path });
        chrome.action.setTitle({ title: title });
    } catch (e) {
        console.error("Failed to set icon", e);
    }
}

function connectToNativeHost() {
    if (nativePort) return;
    
    setExtensionIcon('gray');
    
    try {
        nativePort = chrome.runtime.connectNative(NATIVE_APP_NAME);
        setExtensionIcon('connected');
    } catch (e) {
        console.error("Connection failed", e);
        setExtensionIcon('error');
        startReconnectTimer();
        return;
    }

    nativePort.onMessage.addListener((message) => {
        console.log("Nachricht vom sicheren Desktop-Client erhalten.");
        setExtensionIcon('connected'); // we know it's alive!
        
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
        setExtensionIcon('error');
        startReconnectTimer();
    });
}

function startReconnectTimer() {
    if (!reconnectTimer) {
        console.log("Starte Reconnect-Timer (alle 5 Sekunden)...");
        reconnectTimer = setInterval(() => {
            if (!nativePort) {
                console.log("Versuche Reconnect...");
                connectToNativeHost();
            } else {
                clearInterval(reconnectTimer);
                reconnectTimer = null;
            }
        }, 5000);
    }
}

// Initiale Verbindung aufbauen
connectToNativeHost();

// Lausche auf Anfragen vom Content-Script (aus der Webseite)
chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
    if (request.type === "REQUEST_AUTOFILL") {
        if (!nativePort) connectToNativeHost();
        if (nativePort) {
            nativePort.postMessage({
                Type: "REQUEST_AUTOFILL",
                Url: request.url
            });
        }
    } else if (request.type === "REQUEST_CREDENTIAL_DATA") {
        if (!nativePort) connectToNativeHost();
        if (nativePort) {
            nativePort.postMessage({
                Type: "REQUEST_CREDENTIAL_DATA",
                AssetId: request.assetId
            });
        }
    }
    return true;
});