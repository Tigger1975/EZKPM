/**
 * Stealth Injection Script (Content Script).
 * Läuft im Kontext der Zielwebseite, isoliert von der Haupt-JS-Umgebung der Seite.
 */

// Suchen nach Login- oder Payment-Formularen
function scanForForms() {
    console.log("EZKPM: scanForForms started");
    // 1. Prüfen, ob wir von Schritt 1 (Username) über einen Page-Reload auf Schritt 2 (Password) gekommen sind
    const savedSessionStr = sessionStorage.getItem('ezkpm_active_autofill');
    if (savedSessionStr) {
        console.log("EZKPM: Found savedSessionStr, injecting...");
        const cred = JSON.parse(savedSessionStr);
        
        const passField = document.querySelector('input[type="password"]');
        if (passField) {
            console.log("EZKPM: Zweischritt-Login (Reload) erkannt. Injiziere autorisiertes Passwort...");
            blockUserInput();
            setTimeout(() => performStealthInjection(cred.Username, cred.Password, cred.CustomFields, cred.TotpCode), 200);
            
            // ANTI-FORENSIK: Storage sofort leeren
            sessionStorage.removeItem('ezkpm_active_autofill');
            return;
        } else {
            // TOTP only view check
            const totpFields = Array.from(document.querySelectorAll('input')).filter(el => {
                const nm = (el.name || '').toLowerCase();
                const id = (el.id || '').toLowerCase();
                const autocomplete = (el.getAttribute('autocomplete') || '').toLowerCase();
                return nm.includes('totp') || nm.includes('otp') || nm.includes('2fa') || nm.includes('mfa') || nm.includes('authenticator') || autocomplete === 'one-time-code';
            });
            if (totpFields.length > 0 && cred.TotpCode) {
                console.log("EZKPM: TOTP (Reload) erkannt. Injiziere TOTP...");
                blockUserInput();
                setTimeout(() => performStealthInjection(cred.Username, cred.Password, cred.CustomFields, cred.TotpCode), 200);
                sessionStorage.removeItem('ezkpm_active_autofill');
                return;
            }
            // Fallback: Wenn wir geladen wurden, aber kein Passwortfeld da ist (z.B. Fehlerseite),
            // heben wir den Blocker nach kurzer Zeit wieder auf und brechen den Auto-Flow ab.
            sessionStorage.removeItem('ezkpm_active_autofill');
        }
    }

    const passwordInputs = document.querySelectorAll('input[type="password"]');
    const ccInputs = document.querySelectorAll('input[name*="card"], input[id*="cc"]'); // Heuristik für Payment-Assets (FA 21)
    
    // Nur Textfelder prüfen, die nach Login aussehen, um ständige Anfragen auf Nicht-Login-Seiten zu vermeiden
    const loginTextInputs = document.querySelectorAll('input[name*="user" i], input[name*="login" i], input[name*="email" i], input[name*="kunden" i], input[name*="account" i], input[id*="user" i], input[id*="login" i]');

    console.log(`EZKPM: Found ${passwordInputs.length} password inputs, ${ccInputs.length} CC inputs, ${loginTextInputs.length} login-like text inputs`);

    if (passwordInputs.length > 0 || ccInputs.length > 0 || loginTextInputs.length > 0) {
        console.log("EZKPM: Sending REQUEST_AUTOFILL to background...");
        // Formular gefunden, wir fragen den Background-Worker (und damit den Desktop-Client), 
        // ob wir Credentials für diese URL haben.
        try {
            chrome.runtime.sendMessage({ 
                type: "REQUEST_AUTOFILL", 
                url: window.location.hostname 
            }, (response) => {
                if (chrome.runtime.lastError) {
                    console.error("EZKPM: sendMessage failed:", chrome.runtime.lastError.message);
                } else {
                    console.log("EZKPM: sendMessage succeeded");
                }
            });
        } catch (err) {
            console.error("EZKPM: Exception during sendMessage:", err);
        }
    } else {
        console.log("EZKPM: No inputs found, skipping autofill request.");
    }
    
    // Feature: Password Generator
    injectGeneratorIcon();
}

let pendingInjectionUsername = null; // Speichert den Username, während wir auf Passwort warten

// Antwort vom Background-Worker (bzw. dem nativen C# Client) verarbeiten
chrome.runtime.onMessage.addListener((message) => {
    console.log("EZKPM Content-Script received message:", message.Type);
    if (message.Type === "AUDIT_REQUIRED") {
        alert("EZK-PM Security: " + message.Message);
    } 
    else if (message.Type === "AVAILABLE_CREDENTIALS") {
        const credentials = message.Credentials;
        if (credentials && credentials.length > 0) {
            showInlineLogo(credentials);
        }
    }
    else if (message.Type === "CREDENTIAL_DATA_RESPONSE") {
        // Der Nutzer hat im Desktop-Client den Grund angegeben und bestätigt! (FA 22)
        if (pendingInjectionUsername !== null) {
            sessionStorage.setItem('ezkpm_active_autofill', JSON.stringify({ 
                Username: pendingInjectionUsername, 
                Password: message.Password, 
                TotpCode: message.TotpCode,
                CustomFields: message.CustomFields 
            }));
            blockUserInput();
            performStealthInjection(pendingInjectionUsername, message.Password, message.CustomFields, message.TotpCode);
            pendingInjectionUsername = null;
        }
    }
    else if (message.Type === "AUDIT_REJECTED") {
        alert("EZK-PM Security: Die Herausgabe des Passworts wurde im Desktop-Client abgelehnt oder abgebrochen.");
        pendingInjectionUsername = null;
    }
});

function showInlineLogo(credentials) {
    const isVisible = (el) => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        const style = window.getComputedStyle(el);
        // Echte Eingabefelder sind größer als 10x10 Pixel (verhindert Honeypots/Hidden Fields)
        return rect.width > 0 && rect.height > 0 && style.visibility !== 'hidden' && style.display !== 'none' && el.type !== 'hidden';
    };

    // Priorität 1: Das Username/Login/Email-Feld
    const explicitUserFields = document.querySelectorAll('input[name*="user" i], input[name*="login" i], input[name*="email" i], input[name*="kunden" i], input[name*="account" i], input[id*="user" i], input[id*="login" i]');
    let targetField = Array.from(explicitUserFields).find(isVisible);
    
    // Priorität 2: Sichtbares Passwort-Feld (falls explizite Username-Felder fehlen)
    if (!targetField) {
        targetField = Array.from(document.querySelectorAll('input[type="password"]')).find(isVisible);
    }

    // Wenn weder ein Passwort-Feld noch ein explizites Login-Feld gefunden wurde, brechen wir ab.
    // So verhindern wir, dass das Wappen an generische Suchfelder (z.B. Artikelnummer) angehängt wird.
    if (!targetField) return;

    // Vorhandenes Icon entfernen, falls es an ein altes/verstecktes Feld gehängt war
    const existingIcon = document.getElementById('ezkpm-inline-icon');
    if (existingIcon) existingIcon.remove();

    const icon = document.createElement('img');
    icon.id = 'ezkpm-inline-icon';
    icon.src = chrome.runtime.getURL('icons/icon_green.png');
    icon.style.position = 'absolute';
    icon.style.height = '20px';
    icon.style.cursor = 'pointer';
    icon.style.zIndex = '9999';
    icon.title = `EZKPM Autofill (${credentials.length} verfügbare Konten)`;

    // Position the icon correctly over the input field
    const rect = targetField.getBoundingClientRect();
    icon.style.left = (rect.right - 30 + window.scrollX) + 'px';
    icon.style.top = (rect.top + rect.height / 2 - 10 + window.scrollY) + 'px';
    
    // Fallback if inside a relative container or scrolling happens, 
    // we recalculate on scroll
    const updatePos = () => {
        const newRect = targetField.getBoundingClientRect();
        icon.style.left = (newRect.right - 30 + window.scrollX) + 'px';
        icon.style.top = (newRect.top + newRect.height / 2 - 10 + window.scrollY) + 'px';
    };
    window.addEventListener('scroll', updatePos);
    window.addEventListener('resize', updatePos);

    document.body.appendChild(icon);

    icon.addEventListener('click', (e) => {
        e.preventDefault();
        e.stopPropagation();
        if (credentials.length === 1) {
            // Fordere Passwort beim Desktop-Client an (löst dort Audit-Dialog aus)
            pendingInjectionUsername = credentials[0].Username;
            chrome.runtime.sendMessage({ 
                type: "REQUEST_CREDENTIAL_DATA", 
                assetId: credentials[0].AssetId 
            });
            icon.remove();
        } else {
            showCredentialSelector(credentials, icon);
        }
    });
}

function showCredentialSelector(credentials, anchorElement) {
    let existing = document.getElementById('ezkpm-selector');
    if (existing) { existing.remove(); return; }

    const rect = anchorElement.getBoundingClientRect();
    const dropdown = document.createElement('div');
    dropdown.id = 'ezkpm-selector';
    dropdown.style.position = 'absolute';
    dropdown.style.top = (rect.bottom + 5 + window.scrollY) + 'px';
    dropdown.style.left = (rect.left - 150 + window.scrollX) + 'px';
    dropdown.style.width = '250px';
    dropdown.style.backgroundColor = '#1E293B';
    dropdown.style.border = '1px solid #3B82F6';
    dropdown.style.borderRadius = '5px';
    dropdown.style.zIndex = '10000';
    dropdown.style.boxShadow = '0 4px 6px rgba(0,0,0,0.3)';
    dropdown.style.padding = '5px 0';
    dropdown.style.color = 'white';
    dropdown.style.fontFamily = 'sans-serif';

    credentials.forEach(cred => {
        const item = document.createElement('div');
        item.style.padding = '8px 15px';
        item.style.cursor = 'pointer';
        item.style.borderBottom = '1px solid #334155';
        item.innerHTML = `<strong>${cred.Title}</strong><br><span style="font-size:11px;color:#94A3B8">${cred.Username}</span>`;
        
        item.addEventListener('mouseover', () => item.style.backgroundColor = '#334155');
        item.addEventListener('mouseout', () => item.style.backgroundColor = 'transparent');
        
        item.addEventListener('click', () => {
            pendingInjectionUsername = cred.Username;
            chrome.runtime.sendMessage({ 
                type: "REQUEST_CREDENTIAL_DATA", 
                assetId: cred.AssetId 
            });
            dropdown.remove();
            anchorElement.remove();
        });
        dropdown.appendChild(item);
    });

    document.body.appendChild(dropdown);
    
    setTimeout(() => {
        const closeHandler = (e) => {
            if (!dropdown.contains(e.target) && e.target !== anchorElement) {
                dropdown.remove();
                document.removeEventListener('click', closeHandler);
            }
        };
        document.addEventListener('click', closeHandler);
    }, 100);
}

// Globale Referenzen für den Blocker
let blockerTimeoutId = null;
let preventKeysHandler = null;

function blockUserInput() {
    // Falls ein Blocker bereits existiert (z.B. bei SPA Schritt 2), setzen wir nur den Timer neu auf 5 Sekunden!
    if (document.getElementById('ezkpm-blocker')) {
        if (blockerTimeoutId) clearTimeout(blockerTimeoutId);
        blockerTimeoutId = setTimeout(removeBlocker, 5000);
        return;
    }

    // Erstelle ein unsichtbares (oder sehr leicht abgedunkeltes) Overlay über den gesamten Bildschirm
    const blocker = document.createElement('div');
    blocker.id = 'ezkpm-blocker';
    blocker.style.position = 'fixed';
    blocker.style.top = '0';
    blocker.style.left = '0';
    blocker.style.width = '100vw';
    blocker.style.height = '100vh';
    blocker.style.backgroundColor = 'rgba(0, 0, 0, 0.1)';
    blocker.style.zIndex = '2147483647'; // Maximaler Z-Index
    blocker.style.display = 'flex';
    blocker.style.alignItems = 'flex-end';
    blocker.style.justifyContent = 'center';
    blocker.style.paddingBottom = '15vh';
    blocker.style.cursor = 'wait'; // Lade-Mauszeiger

    const msgBox = document.createElement('div');
    msgBox.style.backgroundColor = '#1E293B';
    msgBox.style.color = '#10B981'; // Grün
    msgBox.style.padding = '10px 20px';
    msgBox.style.borderRadius = '6px';
    msgBox.style.fontFamily = 'sans-serif';
    msgBox.style.fontSize = '14px';
    msgBox.style.fontWeight = 'bold';
    msgBox.style.boxShadow = '0 5px 15px rgba(0,0,0,0.3)';
    msgBox.innerHTML = '🛡️ EZKPM Auto-Login läuft...';
    blocker.appendChild(msgBox);

    document.body.appendChild(blocker);

    // Tastatur-Events blockieren (Capture Phase)
    preventKeysHandler = (e) => {
        if (e.key === 'Escape' || e.key === 'F5') return; // Erlaube Abbruch oder Reload
        e.preventDefault();
        e.stopPropagation();
    };
    window.addEventListener('keydown', preventKeysHandler, true);
    window.addEventListener('keypress', preventKeysHandler, true);
    
    // Nach 5 Sekunden den Blocker wieder entfernen
    blockerTimeoutId = setTimeout(removeBlocker, 5000);
}

function removeBlocker() {
    const blocker = document.getElementById('ezkpm-blocker');
    if (blocker) blocker.remove();
    if (preventKeysHandler) {
        window.removeEventListener('keydown', preventKeysHandler, true);
        window.removeEventListener('keypress', preventKeysHandler, true);
    }
}

let lastInjectedPassword = null;

function performStealthInjection(username, password, customFields = [], totpCode = null, loginFlow = null) {
    let injected = false;
    const injectedFields = new Set(); // Merkt sich, welche Felder wir schon befüllt haben

    // Helper function to find element by explicit selector, fallback to heuristics
    function findField(selector, fallbackFn) {
        if (loginFlow && loginFlow.AutoLearnEnabled && selector) {
            try {
                const el = document.querySelector(selector);
                if (el) return el;
            } catch (e) {}
        }
        return fallbackFn();
    }

    // Hilfsfunktion: Prüft Sichtbarkeit strenger
    const isVisible = (el) => {
        if (!el) return false;
        const r = el.getBoundingClientRect();
        const style = window.getComputedStyle(el);
        return r.width > 0 && r.height > 0 && style.visibility !== 'hidden' && style.display !== 'none';
    };

    // 1. Custom Fields injizieren (z.B. Kundennummer) - Höchste Priorität!
    if (customFields && customFields.length > 0) {
        customFields.forEach(cf => {
            if (!cf.Name || !cf.Value) return;
            
            // Fuzzy-Name für tolerante Suche (z.B. "Kundenummer" vs "Kundennummer")
            const fuzzyName = cf.Name.toLowerCase().replace(/[^a-z0-9]/g, '').replace(/(.)\1+/g, '$1');

            // Suche per name, id, placeholder oder aria-label (exakt)
            let field = document.querySelector(`input[name="${cf.Name}" i], input[id="${cf.Name}" i], input[placeholder*="${cf.Name}" i], input[aria-label*="${cf.Name}" i]`);
            
            // Fallback 1: Suche über Labels (Fuzzy)
            if (!field) {
                const labels = Array.from(document.querySelectorAll('label'));
                const matchingLabel = labels.find(l => l.innerText.toLowerCase().replace(/[^a-z0-9]/g, '').replace(/(.)\1+/g, '$1').includes(fuzzyName));
                if (matchingLabel) {
                    if (matchingLabel.htmlFor) field = document.getElementById(matchingLabel.htmlFor);
                    if (!field) field = matchingLabel.querySelector('input');
                    if (!field && matchingLabel.nextElementSibling && matchingLabel.nextElementSibling.tagName === 'INPUT') {
                        field = matchingLabel.nextElementSibling;
                    }
                    if (!field && matchingLabel.parentElement) {
                        field = matchingLabel.parentElement.querySelector('input');
                    }
                }
            }

            // Fallback 2: DOM-Linearer Scan (Leserichtung)
            // Wenn wir das Wort auf der Webseite sehen, ist das nächste Input-Feld in der Regel das richtige!
            if (!field) {
                try {
                    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_TEXT);
                    let foundText = false;
                    while (walker.nextNode()) {
                        const node = walker.currentNode;
                        
                        if (!foundText && node.nodeType === Node.TEXT_NODE) {
                            if (node.textContent) {
                                const fuzzyText = node.textContent.toLowerCase().replace(/[^a-z0-9]/g, '').replace(/(.)\1+/g, '$1');
                                if (fuzzyText.includes(fuzzyName)) {
                                    foundText = true;
                                }
                            }
                        } 
                        else if (foundText && node.nodeType === Node.ELEMENT_NODE && node.tagName === 'INPUT') {
                            if (isVisible(node) && !['hidden', 'button', 'submit', 'checkbox', 'radio'].includes(node.type.toLowerCase())) {
                                field = node;
                                break;
                            }
                        }
                    }
                } catch (err) {
                    console.error("EZKPM TreeWalker Error:", err);
                }
            }

            if (field) {
                field.value = cf.Value;
                field.dispatchEvent(new Event('input', { bubbles: true }));
                field.dispatchEvent(new Event('change', { bubbles: true }));
                injectedFields.add(field);
                injected = true;
                console.log(`EZKPM: Custom Field '${cf.Name}' erfolgreich befüllt.`);
            } else {
                console.warn(`EZKPM: Custom Field '${cf.Name}' konnte auf der Webseite nicht gefunden werden!`);
            }
        });
    }


    // 2. Standard Username injizieren (überspringt Felder, die schon per CustomField befüllt wurden)
    let userField = findField(loginFlow?.UsernameSelector, () => {
        // A) Explizite Username-Felder bevorzugen
        const explicitUserFields = document.querySelectorAll('input[name*="user" i], input[name*="login" i], input[name*="alias" i], input[name*="account" i], input[id*="user" i], input[id*="login" i], input[type="email"]');
        let field = Array.from(explicitUserFields).find(el => isVisible(el) && !injectedFields.has(el));

        // B) Fallback: Das erste Textfeld in einem Formular
        if (!field) {
            const formInputs = document.querySelectorAll('form input:not([type="hidden"]):not([type="submit"]):not([type="button"]):not([type="checkbox"]):not([type="radio"]):not([type="password"])');
            field = Array.from(formInputs).find(el => isVisible(el) && !injectedFields.has(el));
        }
        return field;
    });

    if (userField && username && userField.value !== username) {
        userField.value = username;
        userField.dispatchEvent(new Event('input', { bubbles: true }));
        userField.dispatchEvent(new Event('change', { bubbles: true }));
        injected = true;
    }

    // 3. Passwort injizieren
    const passField = findField(loginFlow?.PasswordSelector, () => document.querySelector('input[type="password"]'));
    if (passField && password && passField.value !== password) {
        passField.value = password;
        passField.dispatchEvent(new Event('input', { bubbles: true }));
        passField.dispatchEvent(new Event('change', { bubbles: true }));
        injected = true;
        lastInjectedPassword = password;
    }

    // 4. TOTP Code injizieren (falls vorhanden)
    if (totpCode) {
        const totpFields = Array.from(document.querySelectorAll('input')).filter(el => {
            if (!isVisible(el)) return false;
            const nm = (el.name || '').toLowerCase();
            const id = (el.id || '').toLowerCase();
            const autocomplete = (el.getAttribute('autocomplete') || '').toLowerCase();
            return nm.includes('totp') || nm.includes('otp') || nm.includes('2fa') || nm.includes('mfa') || nm.includes('authenticator') || autocomplete === 'one-time-code';
        });
        
        if (totpFields.length > 0) {
            const field = totpFields[0];
            field.value = totpCode;
            field.dispatchEvent(new Event('input', { bubbles: true }));
            field.dispatchEvent(new Event('change', { bubbles: true }));
            injectedFields.add(field);
            injected = true;
        }
    }

    if (injected) {
        console.log("EZKPM: Credentials injected securely.");

        // 1. Strict Observer: Verhindere das Aufdecken des Passworts
        if (passField) {
            const strictObserver = new MutationObserver((mutations) => {
                for (const mutation of mutations) {
                    if (mutation.attributeName === 'type') {
                        if (passField.getAttribute('type') === 'text') {
                            console.warn("EZKPM Security: Webseite versuchte das Passwort aufzudecken. Feld wird geleert!");
                            passField.value = "";
                            passField.dispatchEvent(new Event('input', { bubbles: true }));
                            passField.dispatchEvent(new Event('change', { bubbles: true }));
                        }
                    }
                }
            });
            strictObserver.observe(passField, { attributes: true, attributeFilter: ['type'] });
        }

        // 2. Auto-Submit: Formular automatisch absenden
        setTimeout(() => {
            // Auto-Submit Logic
            let submitted = false;
            
            const submitBtn = findField(loginFlow?.SubmitButtonSelector, () => {
                // Return null intentionally so fallback handles forms gracefully if no strict selector exists
                return null;
            });
            
            // Versuch 1: Falls wir den Submit-Button per AutoLearn haben, klick ihn
            if (submitBtn) {
                submitBtn.click();
                submitted = true;
            }
            
            // Versuch 2: Heuristische Suche nach Anmelde-Buttons im gesamten DOM
            if (!submitted) {
                const buttons = Array.from(document.querySelectorAll('button, input[type="button"], input[type="submit"], div[role="button"], a.button'));
                const loginBtn = buttons.find(b => {
                    const text = (b.innerText || b.value || "").toLowerCase();
                    const id = (b.id || "").toLowerCase();
                    const name = (b.name || "").toLowerCase();
                    const className = (typeof b.className === 'string' ? b.className : "").toLowerCase();
                    const rel = (b.getAttribute('rel') || "").toLowerCase();
                    
                    const combinedStr = `${text} ${id} ${name} ${className} ${rel}`;
                    return combinedStr.includes("login") || 
                           combinedStr.includes("sign in") || 
                           combinedStr.includes("signin") || 
                           combinedStr.includes("anmelden") || 
                           combinedStr.includes("einloggen") || 
                           combinedStr.includes("weiter") || 
                           combinedStr.includes("next");
                });
                
                if (loginBtn) {
                    loginBtn.click();
                    submitted = true;
                }
            }

            // Versuch 3: Das umgebende Formular absenden via submit Button
            if (!submitted && passField && passField.form) {
                const fallbackBtn = passField.form.querySelector('button[type="submit"], input[type="submit"]');
                if (fallbackBtn) {
                    fallbackBtn.click();
                    submitted = true;
                }
            }

            // Versuch 4: Fallback auf hartes Formular-Submit
            if (!submitted && passField && passField.form) {
                try {
                    passField.form.requestSubmit();
                    submitted = true;
                } catch (e) {
                    passField.form.submit();
                    submitted = true;
                }
            }
            
            if (submitted) {
                console.log("EZKPM: Auto-Submit erfolgreich ausgelöst.");
            }
        }, 500); // Kurze Verzögerung, damit Frameworks wie React die Input-Events verarbeiten können
    }
}

// Initiale Ausführung
setTimeout(scanForForms, 1000);

// SPA Support (2-Schritt Logins): Beobachte das DOM auf neue Eingabefelder
const observer = new MutationObserver((mutations) => {
    for (const mutation of mutations) {
        if (mutation.addedNodes.length > 0) {
            const addedPasswordNode = Array.from(mutation.addedNodes).some(node => 
                node.nodeType === 1 && (node.matches('input[type="password"]') || node.querySelector('input[type="password"]'))
            );
            const addedTextNode = Array.from(mutation.addedNodes).some(node => 
                node.nodeType === 1 && (
                    node.matches('input[name*="user" i], input[name*="login" i], input[name*="email" i], input[name*="kunden" i], input[name*="account" i], input[id*="user" i], input[id*="login" i]') || 
                    node.querySelector('input[name*="user" i], input[name*="login" i], input[name*="email" i], input[name*="kunden" i], input[name*="account" i], input[id*="user" i], input[id*="login" i]')
                )
            );

            if (addedPasswordNode || addedTextNode) {
                // Ein Passwort- oder Textfeld ist dynamisch aufgetaucht (SPA Load oder Weiter-Klick).
                const savedSessionStr = sessionStorage.getItem('ezkpm_active_autofill');
                if (savedSessionStr && addedPasswordNode) {
                    const cred = JSON.parse(savedSessionStr);
                    // Wir haben auf Schritt 2 gewartet. Password-Feld ist da!
                    setTimeout(() => {
                        performStealthInjection(cred.Username, cred.Password, cred.CustomFields, cred.TotpCode, cred.LoginFlow);
                    }, 200);
                    // ID für AutoLearn aufbewahren
                    if (cred.AssetId) sessionStorage.setItem('ezkpm_last_injected_asset_id', cred.AssetId);
                    
                    // Cleanup
                    sessionStorage.removeItem('ezkpm_active_autofill');
                } else if (!savedSessionStr) {
                    // Trigger scanForForms to request autofill for the newly added fields
                    scanForForms();
                }
                injectGeneratorIcon();
                break;
            }
        }
    }
});

observer.observe(document.body, { childList: true, subtree: true });

// ==========================================
// FEATURE: Password Generator & Save New Credentials
// ==========================================

function injectGeneratorIcon() {
    const passwordFields = document.querySelectorAll('input[type="password"]');
    passwordFields.forEach(field => {
        // Skip if already has generator or already filled by EZKPM
        if (field.hasAttribute('data-ezkpm-gen') || field.value) return;
        
        field.setAttribute('data-ezkpm-gen', 'true');
        
        const icon = document.createElement('div');
        icon.innerHTML = '🔑';
        icon.style.position = 'absolute';
        icon.style.cursor = 'pointer';
        icon.style.zIndex = '9998';
        icon.title = 'EZKPM: Sicheres Passwort generieren';
        icon.style.fontSize = '16px';
        icon.style.userSelect = 'none';
        
        const updatePos = () => {
            const rect = field.getBoundingClientRect();
            icon.style.left = (rect.right - 55 + window.scrollX) + 'px';
            icon.style.top = (rect.top + rect.height / 2 - 10 + window.scrollY) + 'px';
        };
        window.addEventListener('scroll', updatePos);
        window.addEventListener('resize', updatePos);
        setTimeout(updatePos, 100);
        
        document.body.appendChild(icon);
        
        icon.addEventListener('click', (e) => {
            e.preventDefault();
            e.stopPropagation();
            
            // Toggle UI
            let existingUi = document.getElementById('ezkpm-gen-ui');
            if (existingUi) {
                existingUi.remove();
                return;
            }

            let minLen = field.getAttribute('minlength') || 16;
            minLen = Math.max(16, parseInt(minLen));

            const ui = document.createElement('div');
            ui.id = 'ezkpm-gen-ui';
            ui.style.position = 'absolute';
            ui.style.backgroundColor = '#1E293B';
            ui.style.border = '1px solid #3B82F6';
            ui.style.borderRadius = '8px';
            ui.style.padding = '15px';
            ui.style.zIndex = '10000';
            ui.style.color = 'white';
            ui.style.fontFamily = 'sans-serif';
            ui.style.boxShadow = '0 10px 25px rgba(0,0,0,0.5)';
            ui.style.width = '220px';

            const rect = icon.getBoundingClientRect();
            ui.style.top = (rect.bottom + 5 + window.scrollY) + 'px';
            ui.style.left = (rect.left - 100 + window.scrollX) + 'px';

            ui.innerHTML = `
                <div style="margin-bottom:10px; font-weight:bold; color:#3B82F6 !important;">Passwort Generator</div>
                <div style="margin-bottom:10px;">
                    <label style="font-size:12px; color:white !important;">Länge: <span id="ezkpm-len-val" style="color:white !important;">${minLen}</span></label>
                    <input type="range" id="ezkpm-len" min="8" max="64" value="${minLen}" style="width:100%;">
                </div>
                <div style="font-size:12px; margin-bottom:15px; display:grid; grid-template-columns: 1fr 1fr; gap:5px; color:white !important;">
                    <label style="color:white !important; display:flex; align-items:center; gap:4px;"><input type="checkbox" id="ezkpm-uc" checked> A-Z</label>
                    <label style="color:white !important; display:flex; align-items:center; gap:4px;"><input type="checkbox" id="ezkpm-lc" checked> a-z</label>
                    <label style="color:white !important; display:flex; align-items:center; gap:4px;"><input type="checkbox" id="ezkpm-num" checked> 0-9</label>
                    <label style="color:white !important; display:flex; align-items:center; gap:4px;"><input type="checkbox" id="ezkpm-spec" checked> !@#$</label>
                </div>
                <button id="ezkpm-gen-btn" style="width:100%; background:#10B981; color:white; border:none; padding:8px; border-radius:4px; cursor:pointer; font-weight:bold;">Generieren & Einfüllen</button>
            `;

            document.body.appendChild(ui);

            // Close when clicking outside
            setTimeout(() => {
                const closeHandler = (ev) => {
                    if (!ui.contains(ev.target) && ev.target !== icon) {
                        ui.remove();
                        document.removeEventListener('click', closeHandler);
                    }
                };
                document.addEventListener('click', closeHandler);
            }, 100);

            // Update length label
            const lenSlider = document.getElementById('ezkpm-len');
            const lenVal = document.getElementById('ezkpm-len-val');
            lenSlider.addEventListener('input', () => lenVal.innerText = lenSlider.value);

            // Generate & Fill logic
            document.getElementById('ezkpm-gen-btn').addEventListener('click', (ev) => {
                ev.preventDefault();
                const len = parseInt(lenSlider.value);
                const useUc = document.getElementById('ezkpm-uc').checked;
                const useLc = document.getElementById('ezkpm-lc').checked;
                const useNum = document.getElementById('ezkpm-num').checked;
                const useSpec = document.getElementById('ezkpm-spec').checked;

                let chars = "";
                if (useUc) chars += "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
                if (useLc) chars += "abcdefghijklmnopqrstuvwxyz";
                if (useNum) chars += "0123456789";
                if (useSpec) chars += "!@#$%^&*()_+~`|}{[]:;?><,./-=";
                if (chars === "") chars = "abcdefghijklmnopqrstuvwxyz"; // fallback

                let pwd = "";
                const array = new Uint32Array(len);
                window.crypto.getRandomValues(array);
                for (let i = 0; i < len; i++) {
                    pwd += chars[array[i] % chars.length];
                }

                // Fill into this field
                field.value = pwd;
                field.dispatchEvent(new Event('input', { bubbles: true }));
                field.dispatchEvent(new Event('change', { bubbles: true }));

                // Try to fill confirm-password fields in the same form
                if (field.form) {
                    const allPwd = field.form.querySelectorAll('input[type="password"]');
                    allPwd.forEach(p => {
                        if (p !== field) {
                            p.value = pwd;
                            p.dispatchEvent(new Event('input', { bubbles: true }));
                            p.dispatchEvent(new Event('change', { bubbles: true }));
                        }
                    });
                }

                icon.innerHTML = '✅';
                setTimeout(() => icon.innerHTML = '🔑', 2000);
                sessionStorage.setItem('ezkpm_generated_pwd', pwd);
                ui.remove();
            });
        });
    });
}

function getCssSelector(el) {
    if (!el) return "";
    if (el.id) return '#' + CSS.escape(el.id);
    if (el.name) return `input[name="${CSS.escape(el.name)}"]`;
    let path = el.tagName.toLowerCase();
    if (el.className && typeof el.className === 'string') {
        path += "." + el.className.trim().replace(/\s+/g, '.');
    }
    return path;
}

document.addEventListener('submit', (e) => {
    const form = e.target;
    const pwdField = form.querySelector('input[type="password"]');
    
    const explicitUserFields = form.querySelectorAll('input[name*="user" i], input[name*="login" i], input[name*="alias" i], input[name*="account" i], input[id*="user" i], input[id*="login" i], input[type="email"]');
    let userField = Array.from(explicitUserFields).find(el => el.value);
    if (!userField) {
        const formInputs = form.querySelectorAll('input:not([type="hidden"]):not([type="submit"]):not([type="button"]):not([type="checkbox"]):not([type="radio"]):not([type="password"])');
        userField = Array.from(formInputs).find(el => el.value);
    }
    let username = userField ? userField.value : "";
    let userSelector = getCssSelector(userField);

    if (!pwdField) {
        // Möglicherweise Schritt 1 eines 2-Schritt-Logins (nur Username eingetippt)
        if (username) {
            sessionStorage.setItem('ezkpm_last_typed_user', username);
            sessionStorage.setItem('ezkpm_last_typed_user_sel', userSelector);
        }
        return;
    }
    
    if (pwdField && pwdField.value) {
        if (!username) {
            // Versuche Username aus Schritt 1 wiederherzustellen, falls auf dieser Seite keiner ist
            username = sessionStorage.getItem('ezkpm_last_typed_user') || "";
            userSelector = sessionStorage.getItem('ezkpm_last_typed_user_sel') || "";
        }
        const password = pwdField.value;
        
        let customFields = [];
        const extraInputs = Array.from(form.querySelectorAll('input:not([type="hidden"]):not([type="submit"]):not([type="button"]):not([type="checkbox"]):not([type="radio"]):not([type="password"])'));
        extraInputs.forEach(input => {
            if (input !== userField && input.value) {
                let name = input.name || input.id || input.placeholder || input.getAttribute('aria-label') || "Custom Field";
                customFields.push({ Name: name, Value: input.value });
            }
        });
        
        // Anti-Loop / AutoLearn Feedback:
        if (password === lastInjectedPassword) {
            const assetId = sessionStorage.getItem('ezkpm_last_injected_asset_id');
            if (assetId) {
                try {
                    chrome.runtime.sendMessage({
                        type: "UPDATE_LEARNED_SELECTORS",
                        assetId: assetId,
                        userSelector: userSelector,
                        passSelector: getCssSelector(pwdField),
                        submitSelector: getCssSelector(e.submitter || form.querySelector('button[type="submit"], input[type="submit"]')),
                        customFields: customFields
                    });
                } catch (err) {
                    console.error("EZKPM: Failed to send UPDATE_LEARNED_SELECTORS", err);
                }
            }
            return;
        }
        
        const lastAutofill = sessionStorage.getItem('ezkpm_active_autofill');
        if (lastAutofill) {
            const parsed = JSON.parse(lastAutofill);
            if (parsed.Password === password) return; 
        }
        
        // Confirmation Prompt before sending
        if (!window.confirm(`EZKPM Security\n\nMöchten Sie diese neuen Zugangsdaten für ${window.location.hostname} im Ironclad Vault speichern?\n\nUsername: ${username}`)) {
            return; // User aborted
        }
        
        try {
            chrome.runtime.sendMessage({
                type: "SAVE_NEW_CREDENTIAL",
                url: window.location.hostname,
                username: username,
                password: password,
                userSelector: userSelector,
                passSelector: getCssSelector(pwdField),
                submitSelector: getCssSelector(e.submitter || form.querySelector('button[type="submit"], input[type="submit"]')),
                customFields: customFields
            });
        } catch (err) {
            console.error("EZKPM: Failed to send SAVE_NEW_CREDENTIAL", err);
        }
    }
}, true);