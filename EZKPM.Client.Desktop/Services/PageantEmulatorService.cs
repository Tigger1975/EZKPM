using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.OpenSsl;

namespace EZKPM.Client.Desktop.Services
{
    public class PageantEmulatorService : IHostedService, IDisposable
    {
        private readonly ILogger<PageantEmulatorService> _logger;
        private readonly Func<System.Collections.Generic.IEnumerable<EZKPM.Shared.Contracts.VaultAssetPayload>> _assetProvider;
        private Thread _windowThread;
        private IntPtr _hwnd = IntPtr.Zero;
        private bool _isRunning = false;

        public PageantEmulatorService(ILogger<PageantEmulatorService> logger, Func<System.Collections.Generic.IEnumerable<EZKPM.Shared.Contracts.VaultAssetPayload>> assetProvider)
        {
            _logger = logger;
            _assetProvider = assetProvider;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _isRunning = true;
                _windowThread = new Thread(WindowThreadProc);
                _windowThread.SetApartmentState(ApartmentState.STA);
                _windowThread.IsBackground = true;
                _windowThread.Start();
                _logger.LogInformation("Pageant Emulator gestartet.");
            }
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _isRunning = false;
            if (_hwnd != IntPtr.Zero)
            {
                Win32.SendMessage(_hwnd, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
            return Task.CompletedTask;
        }

        private void WindowThreadProc()
        {
            var wndClass = new Win32.WNDCLASS
            {
                lpszClassName = "Pageant",
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate((Win32.WndProc)WndProc)
            };

            ushort classAtom = Win32.RegisterClass(ref wndClass);
            if (classAtom == 0)
            {
                _logger.LogError($"RegisterClass failed: {Marshal.GetLastWin32Error()}");
                return;
            }

            _hwnd = Win32.CreateWindowEx(
                0,
                "Pageant",
                "Pageant",
                0,
                0, 0, 0, 0,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            if (_hwnd == IntPtr.Zero)
            {
                _logger.LogError($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
                return;
            }

            Win32.MSG msg;
            while (Win32.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                Win32.TranslateMessage(ref msg);
                Win32.DispatchMessage(ref msg);
            }
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == Win32.WM_COPYDATA)
            {
                var cds = Marshal.PtrToStructure<Win32.COPYDATASTRUCT>(lParam);
                if (cds.dwData == new IntPtr(Win32.AGENT_COPYDATA_ID))
                {
                    string mapName = Marshal.PtrToStringAnsi(cds.lpData);
                    ProcessAgentRequest(mapName);
                    return new IntPtr(1); // Return 1 to indicate success
                }
            }
            else if (msg == Win32.WM_CLOSE)
            {
                Win32.DestroyWindow(hWnd);
            }
            else if (msg == Win32.WM_DESTROY)
            {
                Win32.PostQuitMessage(0);
            }

            return Win32.DefWindowProc(hWnd, msg, wParam, lParam);
        }

        private void ProcessAgentRequest(string mapName)
        {
            try
            {
                using var mmf = MemoryMappedFile.OpenExisting(mapName, MemoryMappedFileRights.ReadWrite);
                using var stream = mmf.CreateViewStream(0, 0, MemoryMappedFileAccess.ReadWrite);
                
                byte[] lengthBuf = new byte[4];
                stream.Read(lengthBuf, 0, 4);
                
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(lengthBuf);
                    
                int length = BitConverter.ToInt32(lengthBuf, 0);
                
                if (length <= 0 || length > 8192) return;

                byte[] requestData = new byte[length];
                stream.Read(requestData, 0, length);

                byte requestType = requestData[0];
                
                byte[] responseData;

                switch (requestType)
                {
                    case 11: // SSH2_AGENTC_REQUEST_IDENTITIES
                        responseData = HandleRequestIdentities();
                        break;
                    case 13: // SSH2_AGENTC_SIGN_REQUEST
                        responseData = HandleSignRequest(requestData);
                        break;
                    default:
                        responseData = new byte[] { 5 }; // SSH_AGENT_FAILURE
                        break;
                }

                stream.Position = 0;
                
                byte[] resLength = BitConverter.GetBytes(responseData.Length);
                if (BitConverter.IsLittleEndian) Array.Reverse(resLength);
                
                stream.Write(resLength, 0, 4);
                stream.Write(responseData, 0, responseData.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process Pageant request");
            }
        }

        private byte[] HandleRequestIdentities()
        {
            var sshAssets = _assetProvider?.Invoke().Where(a => a.AssetType == "SSHKey" && !string.IsNullOrWhiteSpace(a.Password) && a.Password.Contains("BEGIN PRIVATE KEY")).ToList() ?? new();

            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            writer.Write((byte)12); // SSH2_AGENT_IDENTITIES_ANSWER
            WriteUInt32(writer, (uint)sshAssets.Count);

            foreach (var asset in sshAssets)
            {
                try
                {
                    using var sr = new StringReader(asset.Password);
                    var pemReader = new PemReader(sr);
                    var keyObj = pemReader.ReadObject();
                    
                    if (keyObj is Ed25519PrivateKeyParameters privKey)
                    {
                        var pubKey = privKey.GeneratePublicKey();
                        byte[] blob = BuildEd25519Blob(pubKey.GetEncoded());
                        
                        WriteSshString(writer, blob);
                        WriteSshString(writer, System.Text.Encoding.UTF8.GetBytes(asset.Title));
                    }
                }
                catch { }
            }

            return ms.ToArray();
        }

        private byte[] HandleSignRequest(byte[] requestData)
        {
            try
            {
                using var msIn = new MemoryStream(requestData);
                using var reader = new BinaryReader(msIn);
                
                reader.ReadByte(); // type 13
                
                byte[] requestedPubKeyBlob = ReadSshString(reader);
                byte[] dataToSign = ReadSshString(reader);
                uint flags = ReadUInt32(reader);

                var sshAssets = _assetProvider?.Invoke().Where(a => a.AssetType == "SSHKey" && !string.IsNullOrWhiteSpace(a.Password) && a.Password.Contains("BEGIN PRIVATE KEY")).ToList() ?? new();

                foreach (var asset in sshAssets)
                {
                    try
                    {
                        using var sr = new StringReader(asset.Password);
                        var pemReader = new PemReader(sr);
                        var keyObj = pemReader.ReadObject();
                        
                        if (keyObj is Ed25519PrivateKeyParameters privKey)
                        {
                            var pubKey = privKey.GeneratePublicKey();
                            byte[] blob = BuildEd25519Blob(pubKey.GetEncoded());
                            
                            if (blob.SequenceEqual(requestedPubKeyBlob))
                            {
                                var signer = new Ed25519Signer();
                                signer.Init(true, privKey);
                                signer.BlockUpdate(dataToSign, 0, dataToSign.Length);
                                byte[] signature = signer.GenerateSignature();

                                byte[] signatureBlob = BuildEd25519SignatureBlob(signature);

                                using var msOut = new MemoryStream();
                                using var writerOut = new BinaryWriter(msOut);
                                writerOut.Write((byte)14); // SSH2_AGENT_SIGN_RESPONSE
                                WriteSshString(writerOut, signatureBlob);
                                return msOut.ToArray();
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return new byte[] { 5 }; // SSH_AGENT_FAILURE
        }

        private byte[] BuildEd25519Blob(byte[] pubKeyBytes)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            WriteSshString(writer, System.Text.Encoding.ASCII.GetBytes("ssh-ed25519"));
            WriteSshString(writer, pubKeyBytes);
            return ms.ToArray();
        }

        private byte[] BuildEd25519SignatureBlob(byte[] sigBytes)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            WriteSshString(writer, System.Text.Encoding.ASCII.GetBytes("ssh-ed25519"));
            WriteSshString(writer, sigBytes);
            return ms.ToArray();
        }

        private void WriteUInt32(BinaryWriter writer, uint val)
        {
            byte[] b = BitConverter.GetBytes(val);
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            writer.Write(b);
        }

        private void WriteSshString(BinaryWriter writer, byte[] data)
        {
            WriteUInt32(writer, (uint)data.Length);
            writer.Write(data);
        }

        private uint ReadUInt32(BinaryReader reader)
        {
            byte[] b = reader.ReadBytes(4);
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            return BitConverter.ToUInt32(b, 0);
        }

        private byte[] ReadSshString(BinaryReader reader)
        {
            uint len = ReadUInt32(reader);
            if (len > 8192) return new byte[0]; // safety
            return reader.ReadBytes((int)len);
        }

        public void Dispose()
        {
            if (_isRunning)
            {
                StopAsync(CancellationToken.None).Wait();
            }
        }
    }

    public static class Win32
    {
        public const uint WM_COPYDATA = 0x004A;
        public const uint WM_CLOSE = 0x0010;
        public const uint WM_DESTROY = 0x0002;
        public const uint AGENT_COPYDATA_ID = 0x804e50ba;

        public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct WNDCLASS
        {
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct COPYDATASTRUCT
        {
            public IntPtr dwData;
            public int cbData;
            public IntPtr lpData;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public System.Drawing.Point pt;
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        public static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        public static extern IntPtr CreateWindowEx(
            uint dwExStyle,
            string lpClassName,
            string lpWindowName,
            uint dwStyle,
            int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern void PostQuitMessage(int nExitCode);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    }
}
