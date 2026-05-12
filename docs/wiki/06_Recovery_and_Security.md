# Recovery & Security Mechanisms

In a Zero-Knowledge system, if a user loses their keys, the Server Administrator *cannot* reset their password or recover their data. Therefore, EZKPM provides robust, decentralized recovery paths.

## 1. Multi-Device Roaming (The 34-Character Secret Key)

As defined in **FA 15**, users are completely independent of the Windows Domain when they want to use EZKPM on a private device (e.g., a Smartphone) or a non-domain laptop.

- The **Secret Key** (e.g., `EZ-XXXXX-XXXXX-XXXXX-XXXXX-XXXXXX`) acts as the Master Password for these devices. 
- It can be viewed at any time in the Desktop UI under **Settings (Gear Icon) -> Sicherheit & Notfall-Wiederherstellung**.
- When installing the mobile app or logging in on a new device, the user provides this key (or scans a QR code). This allows the new device to decrypt the Post-Quantum AD/TPM blobs downloaded from the Server.

## 2. Administrator Break-Glass Recovery (Multi-Owner Principle)

If an employee suddenly leaves the company or loses *both* their computer and their 34-character Secret Key, the Vault would normally be lost forever.
To prevent data loss of corporate assets, EZKPM utilizes **Shamir's Secret Sharing (Multi-Owner Principle)**.

1. **Initiation:** An Admin goes to the **"Fremd-Recovery initiieren"** tab and requests recovery for the lost user's SID.
2. **Approval:** The Server alerts all other Global Admins. 
3. **Threshold:** The request requires cryptographic approval (Share Sending) from **at least 2 independent Administrators**. 
4. **Decryption:** Once 2 shares are provided, the requesting Admin's Client can mathematically reconstruct the lost user's Root Key and salvage the shared corporate folders.

## 3. Mandatory Audit Logs & Hash-Chaining

For highly critical data (Payment Assets, Corporate Social Media Accounts), accountability is essential (**FA 22**).

- When a user accesses a Payment Asset, they must input a Justification (Reason) and an Amount.
- The Desktop Client locally encrypts this interaction and calculates a cryptographic hash, linking it to the *previous* log entry (Hash-Chaining).
- This chain is sent to the Server.
- If a Database Administrator attempts to delete a log entry to cover their tracks, the cryptographic chain breaks. The Desktop Client validates the chain on every pull and will immediately sound a "Tamper Alarm" if manipulation is detected.

## 4. Anti-Forensics (Zero-RAM)

EZKPM employs extreme measures to prevent malware from extracting passwords from your computer's RAM:
- The `SecureMemory` class pins sensitive byte arrays so the garbage collector does not move them, preventing them from being written to the Windows Pagefile (Swap).
- As soon as an operation (like Autofill) is complete, the memory is explicitly zeroed out (`CryptographicOperations.ZeroMemory`).
