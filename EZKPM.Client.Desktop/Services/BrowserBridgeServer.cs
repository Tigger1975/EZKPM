using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EZKPM.Shared.Contracts;

namespace EZKPM.Client.Desktop.Services
{
    public class BrowserBridgeServer
    {
        private static void Log(string msg) => File.AppendAllText(@"C:\Users\adm-kh\ezkpm_bridge_server.log", $"[{DateTime.Now:HH:mm:ss}] {msg}\n");
        private readonly Func<IEnumerable<VaultAssetPayload>> _getDecryptedAssetsFunc;
        private readonly Func<Guid, Task<bool>> _requestAuditFunc;
        private CancellationTokenSource _cts;

        public Action<string> OnCredentialProvided { get; set; }

        public BrowserBridgeServer(Func<IEnumerable<VaultAssetPayload>> getDecryptedAssetsFunc, Func<Guid, Task<bool>> requestAuditFunc)
        {
            _getDecryptedAssetsFunc = getDecryptedAssetsFunc;
            _requestAuditFunc = requestAuditFunc;
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
                    using var pipeServer = new NamedPipeServerStream("EZKPM_BrowserBridge_Pipe", PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    
                    Log("Waiting for connection...");
                    await pipeServer.WaitForConnectionAsync(token);
                    Log("Client connected!");

                    // Verhindere synchrones Blockieren beim BOM-Check komplett!
                    var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                    using var reader = new StreamReader(pipeServer, utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
                    using var writer = new StreamWriter(pipeServer, utf8NoBom, bufferSize: 1024, leaveOpen: true);
                    writer.AutoFlush = true;

                    Log("Reading request...");
                    using var readCts = new CancellationTokenSource(3000);
                    string requestJson = await reader.ReadLineAsync(readCts.Token);
                    Log($"Received request: {requestJson}");
                    if (!string.IsNullOrEmpty(requestJson))
                    {
                        requestJson = requestJson.TrimStart('\uFEFF'); // BOM entfernen, falls vorhanden
                        Log("Processing request...");
                        string responseJson = await ProcessRequestAsync(requestJson);
                        Log($"Sending response: {responseJson}");
                        await writer.WriteLineAsync(responseJson);
                        Log("Response sent.");
                    }
                    
                    Log("Disconnecting client...");
                    pipeServer.Disconnect();
                    Log("Client disconnected.");
                }
                catch (TaskCanceledException) 
                { 
                    Log("Request reading timed out."); 
                }
                catch (OperationCanceledException) 
                { 
                    Log("Server stopped."); 
                    break; 
                }
                catch (Exception ex)
                {
                    File.AppendAllText(@"C:\Users\adm-kh\ezkpm_bridge_error.log", $"[{DateTime.Now:HH:mm:ss}] BrowserBridgeServer Error: {ex.Message}\n{ex.StackTrace}\n");
                }
            }
        }

        private async Task<string> ProcessRequestAsync(string requestJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(requestJson);
                var root = doc.RootElement;
                string action = root.GetProperty("Type").GetString();

                if (action == "REQUEST_AUTOFILL")
                {
                    string url = root.GetProperty("Url").GetString()?.ToLowerInvariant() ?? "";
                    
                    var assets = _getDecryptedAssetsFunc();
                    
                    var matches = assets.Where(a => 
                    {
                        if (a.AssetType != "Login" || string.IsNullOrEmpty(a.Url)) return false;
                        if (a.IsExpired) return false; // FA 30: Block expired assets from autofill
                        if (a.IsDeleted) return false; // Papierkorb assets should not be autofilled
                        
                        // Parse requested URL (Browser)
                        string reqUrl = url.StartsWith("http") ? url : "https://" + url;
                        if (!Uri.TryCreate(reqUrl, UriKind.Absolute, out var reqUri)) return false;

                        // Parse asset URL
                        string assetUrl = a.Url.ToLowerInvariant();
                        if (!assetUrl.StartsWith("http")) assetUrl = "https://" + assetUrl;
                        if (!Uri.TryCreate(assetUrl, UriKind.Absolute, out var assetUri)) return false;

                        return reqUri.Scheme == assetUri.Scheme && reqUri.Host == assetUri.Host;
                    }).ToList();

                    if (matches.Any())
                    {
                        var list = matches.Select(m => new {
                            AssetId = m.TransientAssetId, // Nur ID, kein Passwort! (FA 22)
                            Title = m.Title,
                            Username = m.Username
                        }).ToList();
                        
                        return JsonSerializer.Serialize(new { 
                            Type = "AVAILABLE_CREDENTIALS",
                            Credentials = list
                        });
                    }
                    return JsonSerializer.Serialize(new { Type = "NO_MATCH" });
                }
                else if (action == "REQUEST_CREDENTIAL_DATA")
                {
                    if (root.TryGetProperty("AssetId", out var idProp) && Guid.TryParse(idProp.GetString(), out Guid assetId))
                    {
                        var asset = _getDecryptedAssetsFunc().FirstOrDefault(a => a.TransientAssetId == assetId);
                        if (asset != null)
                        {
                            bool isApproved = true;

                            // FA 22: Audit Log enforcing ONLY for Payment assets!
                            if (asset.AssetType == "Payment")
                            {
                                isApproved = await _requestAuditFunc(assetId);
                            }
                            
                            if (isApproved)
                            {
                                OnCredentialProvided?.Invoke(asset.Title);
                                return JsonSerializer.Serialize(new {
                                    Type = "CREDENTIAL_DATA_RESPONSE",
                                    Password = asset.Password,
                                    CustomFields = asset.CustomFields != null ? asset.CustomFields.Select(cf => new {
                                        Name = cf.Name,
                                        Value = cf.Value
                                    }).ToList() : null
                                });
                            }
                        }
                        return JsonSerializer.Serialize(new { Type = "AUDIT_REJECTED" });
                    }
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
