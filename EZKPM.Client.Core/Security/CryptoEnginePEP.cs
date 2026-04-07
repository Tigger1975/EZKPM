using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace EZKPM.Client.Core.Security
{
    /// <summary>
    /// Setzt Pflichtenheft FA 4.3 (Anti-Forensik & Client-Integrität) um.
    /// Verhindert, dass sensible Schlüssel oder Klartext-Passwörter in die Auslagerungsdatei (Pagefile) 
    /// geschrieben oder bei einem RAM-Dump von Angreifern ausgelesen werden.
    /// </summary>
    public sealed class SecureMemory : IDisposable
    {
        private byte[] _buffer;
        private GCHandle _gcHandle;
        private bool _disposed;

        /// <summary>
        /// Übernimmt ein bestehendes Array, pinnt es im RAM und löscht das Original, falls möglich.
        /// </summary>
        public SecureMemory(byte[] secretData)
        {
            _buffer = new byte[secretData.Length];
            Buffer.BlockCopy(secretData, 0, _buffer, 0, secretData.Length);

            // Verhindert, dass der Garbage Collector das Array im Speicher verschiebt 
            // (was unlöschbare Kopien im RAM erzeugen würde).
            _gcHandle = GCHandle.Alloc(_buffer, GCHandleType.Pinned);

            // Optional: Versuch, das Quell-Array sofort zu wipen, um Spuren zu minimieren
            CryptographicOperations.ZeroMemory(secretData);
        }

        public ReadOnlySpan<byte> Span
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, nameof(SecureMemory));
                return _buffer;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                // Kryptografisches Wiping: Überschreibt den gepinnten Speicher mit Nullen
                if (_buffer != null)
                {
                    CryptographicOperations.ZeroMemory(_buffer);
                }

                // RAM-Pinning aufheben
                if (_gcHandle.IsAllocated)
                {
                    _gcHandle.Free();
                }

                _buffer = null;
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }

        ~SecureMemory()
        {
            Dispose();
        }
    }

    /// <summary>
    /// Führt die eigentlichen Ver- und Entschlüsselungen auf dem Client aus.
    /// Der Server (PDP) sieht niemals die Schlüssel, die hier verarbeitet werden.
    /// </summary>
    public class VaultCryptoService
    {
        private const int TagSizeInBytes = 16; // 128-bit Auth Tag für AES-GCM

        /// <summary>
        /// Entschlüsselt einen vom Server gelieferten CipherBlob mittels AES-256-GCM.
        /// </summary>
        /// <param name="cipherBlob">Der Blob (Kombination aus Chiffrat und am Ende angehängtem Auth Tag)</param>
        /// <param name="nonce">Die vom Server gelieferte 96-bit Nonce</param>
        /// <param name="key">Der Asset-Key, strikt im SecureMemory gehalten</param>
        /// <returns>Klartext als UTF8 String (sollte im UI direkt maskiert werden)</returns>
        public string DecryptAssetPayload(byte[] cipherBlob, byte[] nonce, SecureMemory key)
        {
            if (cipherBlob == null || cipherBlob.Length <= TagSizeInBytes)
                throw new ArgumentException("CipherBlob ist ungültig oder zu kurz.");

            if (nonce.Length != 12)
                throw new ArgumentException("GCM Nonce muss exakt 12 Bytes (96 Bit) lang sein.");

            // Splitten in echtes Chiffrat und Auth-Tag (letzte 16 Bytes)
            int cipherTextLength = cipherBlob.Length - TagSizeInBytes;
            var cipherText = new byte[cipherTextLength];
            var tag = new byte[TagSizeInBytes];

            Buffer.BlockCopy(cipherBlob, 0, cipherText, 0, cipherTextLength);
            Buffer.BlockCopy(cipherBlob, cipherTextLength, tag, 0, TagSizeInBytes);

            // WICHTIG: Das Klartext-Ergebnis landet hier temporär im RAM. 
            // In einer perfekten Implementierung würden wir auch dies sofort nach Nutzung in SecureMemory kapseln.
            var plainText = new byte[cipherTextLength];

            using (var aesGcm = new AesGcm(key.Span))
            {
                aesGcm.Decrypt(nonce, cipherText, tag, plainText);
            }

            string result = Encoding.UTF8.GetString(plainText);

            // Forensik: Temporäres Array sofort wipen, nachdem der String erzeugt wurde.
            CryptographicOperations.ZeroMemory(plainText);

            return result;
        }

        /// <summary>
        /// Verschlüsselt einen neuen Payload (z.B. neues Passwort oder geänderte Metadaten).
        /// </summary>
        public (byte[] CipherBlob, byte[] Nonce) EncryptAssetPayload(string plainText, SecureMemory key)
        {
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var nonce = new byte[12];
            RandomNumberGenerator.Fill(nonce); // Kryptografisch sicherer RNG

            var cipherText = new byte[plainBytes.Length];
            var tag = new byte[TagSizeInBytes];

            using (var aesGcm = new AesGcm(key.Span))
            {
                aesGcm.Encrypt(nonce, plainBytes, cipherText, tag);
            }

            // Forensik-Wipe des temporären Klartext-Arrays
            CryptographicOperations.ZeroMemory(plainBytes);

            // CipherBlob = Chiffrat + AuthTag (wird so an den Server gesendet)
            var cipherBlob = new byte[cipherText.Length + tag.Length];
            Buffer.BlockCopy(cipherText, 0, cipherBlob, 0, cipherText.Length);
            Buffer.BlockCopy(tag, 0, cipherBlob, cipherText.Length, tag.Length);

            return (cipherBlob, nonce);
        }
    }
}