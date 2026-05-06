using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using EZKPM.Shared.Contracts;
using System.Management;
using System.Text.RegularExpressions;

namespace EZKPM.Client.Desktop.Services
{
    public class LocalAppApprovalResult
    {
        public bool IsApproved { get; set; }
        public bool RememberTrust { get; set; }
    }

    public class LocalCredentialsBroker
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool GetNamedPipeClientProcessId(SafePipeHandle Pipe, out uint ClientProcessId);

        [DllImport("shell32.dll", SetLastError = true)]
        static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string lpCmdLine, out int pNumArgs);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr LocalFree(IntPtr hMem);

        private static void Log(string msg) => File.AppendAllText(@"C:\Users\adm-kh\ezkpm_localbroker.log", $"[{DateTime.Now:HH:mm:ss}] {msg}\n");
        private readonly Func<IEnumerable<VaultAssetPayload>> _getDecryptedAssetsFunc;
        private readonly Func<string, string, string, Task<LocalAppApprovalResult>> _requestApprovalFunc;
        private CancellationTokenSource _cts;

        private readonly string _trustStorePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EZKPM", "local_trust.dat");
        private Dictionary<string, HashSet<string>> _trustedAppsCache = new();

        public LocalCredentialsBroker(Func<IEnumerable<VaultAssetPayload>> getDecryptedAssetsFunc, Func<string, string, string, Task<LocalAppApprovalResult>> requestApprovalFunc)
        {
            _getDecryptedAssetsFunc = getDecryptedAssetsFunc;
            _requestApprovalFunc = requestApprovalFunc;
            LoadTrustStore();
        }

        private void LoadTrustStore()
        {
            try
            {
                if (File.Exists(_trustStorePath))
                {
                    byte[] encrypted = File.ReadAllBytes(_trustStorePath);
                    byte[] decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                    string json = Encoding.UTF8.GetString(decrypted);
                    _trustedAppsCache = JsonSerializer.Deserialize<Dictionary<string, HashSet<string>>>(json) ?? new();
                }
            }
            catch { _trustedAppsCache = new(); }
        }

        private void SaveTrustStore()
        {
            try
            {
                string json = JsonSerializer.Serialize(_trustedAppsCache);
                byte[] decrypted = Encoding.UTF8.GetBytes(json);
                byte[] encrypted = ProtectedData.Protect(decrypted, null, DataProtectionScope.CurrentUser);
                Directory.CreateDirectory(Path.GetDirectoryName(_trustStorePath));
                File.WriteAllBytes(_trustStorePath, encrypted);
            }
            catch (Exception ex) { Log($"Failed to save trust store: {ex.Message}"); }
        }

        private string ComputeFileHash(string filePath)
        {
            try
            {
                using var sha256 = SHA256.Create();
                using var stream = File.OpenRead(filePath);
                return Convert.ToBase64String(sha256.ComputeHash(stream));
            }
            catch { return null; }
        }

        private string GetProcessCommandLine(int pid)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher($"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
                using var objects = searcher.Get();
                var obj = objects.Cast<ManagementBaseObject>().SingleOrDefault();
                return obj?["CommandLine"]?.ToString();
            }
            catch { return null; }
        }

        private string[] ParseCommandLineArgs(string cmdLine)
        {
            if (string.IsNullOrWhiteSpace(cmdLine)) return Array.Empty<string>();
            var argv = CommandLineToArgvW(cmdLine, out int argc);
            if (argv == IntPtr.Zero) return Array.Empty<string>();
            try
            {
                string[] args = new string[argc];
                for (int i = 0; i < argc; i++)
                {
                    IntPtr ptr = Marshal.ReadIntPtr(argv, i * IntPtr.Size);
                    args[i] = Marshal.PtrToStringUni(ptr);
                }
                return args;
            }
            finally
            {
                LocalFree(argv);
            }
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => ListenLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
        }

        private async Task ListenLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var pipeServer = new NamedPipeServerStream("EZKPM_LocalBroker_Pipe", PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    
                    Log("Waiting for local client connection...");
                    await pipeServer.WaitForConnectionAsync(token);
                    Log("Local client connected!");

                    var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                    using var reader = new StreamReader(pipeServer, utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
                    using var writer = new StreamWriter(pipeServer, utf8NoBom, bufferSize: 1024, leaveOpen: true);
                    writer.AutoFlush = true;

                    Log("Reading request...");
                    using var readCts = new CancellationTokenSource(5000);
                    string requestJson = await reader.ReadLineAsync(readCts.Token);
                    Log($"Received request: {requestJson}");
                    
                    if (!string.IsNullOrEmpty(requestJson))
                    {
                        requestJson = requestJson.TrimStart('\uFEFF');
                        string responseJson = await ProcessRequestAsync(pipeServer, requestJson);
                        await writer.WriteLineAsync(responseJson);
                    }
                    
                    pipeServer.Disconnect();
                }
                catch (TaskCanceledException) { }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log($"Error: {ex.Message}");
                }
            }
        }

        private async Task<string> ProcessRequestAsync(NamedPipeServerStream pipeServer, string requestJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(requestJson);
                var root = doc.RootElement;
                string action = root.GetProperty("Type").GetString();

                if (action == "REQUEST_CREDENTIAL_BY_TITLE")
                {
                    string title = root.GetProperty("Title").GetString();
                    
                    var asset = _getDecryptedAssetsFunc().FirstOrDefault(a => string.Equals(a.Title, title, StringComparison.OrdinalIgnoreCase) && !a.IsDeleted && !a.IsExpired);
                    if (asset != null)
                    {
                        // 1. Identify Client Process
                        string processName = "Lokale Applikation";
                        string processHash = null;
                        string warningText = null;
                        
                        if (GetNamedPipeClientProcessId(pipeServer.SafePipeHandle, out uint pid))
                        {
                            try
                            {
                                var proc = Process.GetProcessById((int)pid);
                                string exePath = proc.MainModule?.FileName;
                                if (!string.IsNullOrEmpty(exePath))
                                {
                                    processName = Path.GetFileName(exePath);
                                    processHash = ComputeFileHash(exePath);
                                    
                                    string cmdLine = GetProcessCommandLine((int)pid);
                                    string[] args = ParseCommandLineArgs(cmdLine);
                                    
                                    List<string> attachedFiles = new();
                                    
                                    // Skip args[0] as it's usually the executable itself
                                    for (int i = 1; i < args.Length; i++)
                                    {
                                        string argPath = args[i];
                                        // Ignore common switch prefixes to avoid accidental File.Exists on relative things
                                        if (argPath.StartsWith("-") || argPath.StartsWith("/")) continue;
                                        
                                        try
                                        {
                                            if (File.Exists(argPath))
                                            {
                                                string argHash = ComputeFileHash(argPath);
                                                if (!string.IsNullOrEmpty(argHash))
                                                {
                                                    processHash += "|" + argHash;
                                                    attachedFiles.Add(Path.GetFileName(argPath));
                                                }
                                            }
                                        }
                                        catch { }
                                    }

                                    if (attachedFiles.Any())
                                    {
                                        processName += $" (+ {string.Join(", ", attachedFiles)})";
                                    }
                                }
                            }
                            catch { }
                        }

                        // 2. Check Trust
                        bool isTrusted = false;
                        if (processHash != null && _trustedAppsCache.TryGetValue(processHash, out var allowedAssets))
                        {
                            if (allowedAssets.Contains(asset.Title))
                            {
                                isTrusted = true;
                                Log($"Auto-Approve for trusted app hash {processHash} on {asset.Title}");
                            }
                        }
                        
                        if (isTrusted)
                        {
                            return JsonSerializer.Serialize(new {
                                Type = "CREDENTIAL_RESPONSE",
                                Username = asset.Username,
                                Password = asset.Password
                            });
                        }

                        // 3. Request UI Approval
                        var result = await _requestApprovalFunc(processName, asset.Title, warningText);
                        if (result.IsApproved)
                        {
                            if (result.RememberTrust && processHash != null)
                            {
                                if (!_trustedAppsCache.ContainsKey(processHash))
                                    _trustedAppsCache[processHash] = new HashSet<string>();
                                
                                _trustedAppsCache[processHash].Add(asset.Title);
                                SaveTrustStore();
                                Log($"Saved trust for hash {processHash} -> {asset.Title}");
                            }

                            return JsonSerializer.Serialize(new {
                                Type = "CREDENTIAL_RESPONSE",
                                Username = asset.Username,
                                Password = asset.Password
                            });
                        }
                        return JsonSerializer.Serialize(new { Type = "ACCESS_DENIED" });
                    }
                    return JsonSerializer.Serialize(new { Type = "NOT_FOUND" });
                }

                return JsonSerializer.Serialize(new { error = "Unknown action" });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }
    }
}
