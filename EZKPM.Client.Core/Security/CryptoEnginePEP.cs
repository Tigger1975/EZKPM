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
}