# Browser Extension & Native Messaging

The EZKPM Browser Extension is the primary way users interact with their passwords during web browsing. 
It strictly follows the **Zero-Knowledge** and **Zero-RAM** paradigms.

## 1. Native Messaging Bridge
Traditional password manager extensions often download encrypted vaults from the cloud and decrypt them directly inside the browser using JavaScript. This exposes the vault to malicious browser extensions or cross-site scripting (XSS) attacks.

EZKPM uses a **Native Messaging Bridge** (Manifest V3):
- The browser extension contains **no passwords** and **no cryptographic logic**.
- When you visit a login page, the extension sends the URL over a secure, local operating system pipe (`stdio`) to the EZKPM Desktop Client running in the background.
- The Desktop Client performs the high-security decryption in its protected memory space.
- The Client sends *only* the specific username and password for that exact URL back through the pipe, where it is instantly injected into the webpage by the extension.

## 2. Stealth Injection (Execute-Only)
For high-security shared accounts (e.g., a shared corporate Twitter account), administrators can grant you `Execute-Only` permissions.
- In this mode, the Desktop Client will never show you the actual password string. 
- You cannot copy it.
- When the Browser Extension requests the password, the Native Bridge injects it directly into the DOM elements without exposing it to the user's clipboard.

## 3. Installation
The Desktop Client automatically registers the Native Messaging Host (`com.ezkpm.nativehost`) in the Windows Registry during startup (or via the `--autostart` argument) and installs the extension in Google Chrome and Microsoft Edge automatically.
No manual setup is required by the user.

## 4. Troubleshooting the Extension
If Autofill is not working:
- Ensure the EZKPM Desktop Client is running (check the System Tray).
- Verify the extension is enabled in `chrome://extensions` or `edge://extensions`.
- If debugging is necessary, start the desktop client with the `--verbose` or `--local-logs` flag. The Desktop Client will output communication logs to `%LocalAppData%\EZKPM\ezkpm_nativehost.log`. By default, to prevent performance issues and file clutter, local logging is disabled.
