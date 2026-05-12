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
    public class BrowserBridgeServer(Func<IEnumerable<VaultAssetPayload>> getDecryptedAssetsFunc, Func<Guid, bool, string, Task<bool>> requestAuditFunc, Func<Task<bool>> requestUnlockFunc)
    {
        private static void Log(string msg)
        {
            try 
            { 
                var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EZKPM");
                Directory.CreateDirectory(logDir);
                File.AppendAllText(Path.Combine(logDir, "ezkpm_bridge_server.log"), $"[{DateTime.Now:HH:mm:ss}] {msg}\n"); 
            } 
            catch { }
        }
        private readonly Func<IEnumerable<VaultAssetPayload>> _getDecryptedAssetsFunc = getDecryptedAssetsFunc;
        private readonly Func<Guid, bool, string, Task<bool>> _requestAuditFunc = requestAuditFunc;
        private readonly Func<Task<bool>> _requestUnlockFunc = requestUnlockFunc;
        private CancellationTokenSource _cts;

        public Action<string> OnCredentialProvided { get; set; }
        public Action<VaultAssetPayload> OnSaveNewCredentialRequested { get; set; }

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
                    try 
                    {
                        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EZKPM");
                        File.AppendAllText(Path.Combine(logDir, "ezkpm_bridge_error.log"), $"[{DateTime.Now:HH:mm:ss}] BrowserBridgeServer Error: {ex.Message}\n{ex.StackTrace}\n");
                    } catch { }
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
                    if (!await _requestUnlockFunc()) return JsonSerializer.Serialize(new { Type = "NO_MATCH" });

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

                    if (matches.Count != 0)
                    {
                        var list = matches.Select(m => new {
                            AssetId = m.TransientAssetId, // Nur ID, kein Passwort! (FA 22)
                            m.Title,
                            m.Username
                        }).ToList();
                        
                        return JsonSerializer.Serialize(new { 
                            Type = "AVAILABLE_CREDENTIALS",
                            Credentials = list
                        });
                    }
                    return JsonSerializer.Serialize(new { Type = "NO_MATCH" });
                }
                else if (action == "REQUEST_SEARCH")
                {
                    if (!await _requestUnlockFunc()) return JsonSerializer.Serialize(new { Type = "SEARCH_RESULTS", Results = new List<object>() });

                    string query = root.TryGetProperty("Query", out var qProp) ? qProp.GetString() : "";
                    var assets = _getDecryptedAssetsFunc();
                    var matches = assets.Where(a => 
                    {
                        if (a.IsDeleted) return false;
                        if (a.AssetType == "Folder") return false;
                        if (string.IsNullOrEmpty(query)) return true;
                        return (a.Title?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) || 
                               (a.Url?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) || 
                               (a.Username?.Contains(query, StringComparison.OrdinalIgnoreCase) == true);
                    }).Take(20).ToList();

                    var list = matches.Select(m => new {
                        AssetId = m.TransientAssetId,
                        m.Title,
                        m.Username,
                        m.AssetType,
                        m.Url
                    }).ToList();
                    
                    return JsonSerializer.Serialize(new { 
                        Type = "SEARCH_RESULTS",
                        Results = list
                    });
                }
                else if (action == "REQUEST_CREDENTIAL_DATA")
                {
                    if (!await _requestUnlockFunc()) return JsonSerializer.Serialize(new { Type = "AUDIT_REJECTED" });

                    if (root.TryGetProperty("AssetId", out var idProp) && Guid.TryParse(idProp.GetString(), out Guid assetId))
                    {
                        var asset = _getDecryptedAssetsFunc().FirstOrDefault(a => a.TransientAssetId == assetId);
                        if (asset != null)
                        {
                            bool isApproved = true;

                            // FA 22: Audit Log enforcing ONLY for Payment assets!
                            // New: Silent Audit Logging for assets flagged with RequiresAuditLog
                            if (asset.AssetType == "Payment")
                            {
                                isApproved = await _requestAuditFunc(assetId, true, null);
                            }
                            else if (asset.RequiresAuditLog)
                            {
                                isApproved = await _requestAuditFunc(assetId, false, "Autofill / Password fetch via Browser Extension");
                            }
                            
                            if (isApproved)
                            {
                                OnCredentialProvided?.Invoke(asset.Title);
                                return JsonSerializer.Serialize(new {
                                    Type = "CREDENTIAL_DATA_RESPONSE",
                                    Password = asset.Password,
                                    TotpCode = !string.IsNullOrEmpty(asset.TotpSecret) ? EZKPM.Client.Desktop.Views.AssetEditorWindow.GetTotpCode(asset.TotpSecret) : null,
                                    asset.LoginFlow,
                                    CustomFields = asset.CustomFields?.Select(cf => new {
                                        cf.Name,
                                        cf.Value
                                    }).ToList()
                                });
                            }
                        }
                        return JsonSerializer.Serialize(new { Type = "AUDIT_REJECTED" });
                    }
                }
                else if (action == "SAVE_NEW_CREDENTIAL")
                {
                    string url = root.TryGetProperty("Url", out var urlProp) ? urlProp.GetString() : "";
                    string username = root.TryGetProperty("Username", out var userProp) ? userProp.GetString() : "";
                    string password = root.TryGetProperty("Password", out var pwdProp) ? pwdProp.GetString() : "";
                    string userSel = root.TryGetProperty("UserSelector", out var uSelProp) ? uSelProp.GetString() : "";
                    string passSel = root.TryGetProperty("PassSelector", out var pSelProp) ? pSelProp.GetString() : "";
                    string submitSel = root.TryGetProperty("SubmitSelector", out var subSelProp) ? subSelProp.GetString() : "";
                    
                    var payload = new VaultAssetPayload
                    {
                        AssetType = "Login",
                        Title = string.IsNullOrEmpty(url) ? "New Login" : url + " Login",
                        Url = url,
                        Username = username,
                        Password = password,
                        LoginFlow = new LoginFlowConfig
                        {
                            AutoLearnEnabled = true,
                            Method = "AutoLearn",
                            UsernameSelector = userSel,
                            PasswordSelector = passSel,
                            SubmitButtonSelector = submitSel
                        }
                    };

                    if (root.TryGetProperty("CustomFields", out var cfArray) && cfArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var cf in cfArray.EnumerateArray())
                        {
                            payload.CustomFields.Add(new CustomField
                            {
                                Name = cf.TryGetProperty("Name", out var nProp) ? nProp.GetString() : "",
                                Value = cf.TryGetProperty("Value", out var vProp) ? vProp.GetString() : ""
                            });
                        }
                    }

                    OnSaveNewCredentialRequested?.Invoke(payload);
                    return JsonSerializer.Serialize(new { Type = "ACK" });
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
