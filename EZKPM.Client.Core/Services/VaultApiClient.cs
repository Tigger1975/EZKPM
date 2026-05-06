using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using EZKPM.Shared.Contracts;

namespace EZKPM.Client.Core.Services
{
    public class VaultApiClient
    {
        private readonly HttpClient _httpClient;

        public VaultApiClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<VaultAssetResponseDto> GetAssetAsync(Guid assetId)
        {
            var response = await _httpClient.GetAsync($"/api/v1/vault/assets/{assetId}");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<VaultAssetResponseDto>();
        }

        public async Task<System.Collections.Generic.List<VaultAssetResponseDto>> GetAllAssetsAsync()
        {
            var response = await _httpClient.GetAsync("/api/v1/vault/assets/all");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<System.Collections.Generic.List<VaultAssetResponseDto>>();
        }

        public async Task<Guid> CreateAssetAsync(CreateAssetRequestDto request)
        {
            var response = await _httpClient.PostAsJsonAsync("/api/v1/vault/assets", request);
            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                var err = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                throw new Exception(err.GetProperty("error").GetString());
            }
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            return result.GetProperty("assetId").GetGuid();
        }

        public async Task UpdateAssetAsync(Guid id, CreateAssetRequestDto request)
        {
            var response = await _httpClient.PutAsJsonAsync($"/api/v1/vault/assets/{id}", request);
            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                var err = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                throw new Exception(err.GetProperty("error").GetString());
            }
            response.EnsureSuccessStatusCode();
        }

        public async Task DeleteAssetAsync(Guid id, bool forceAdmin = false)
        {
            var url = $"/api/v1/vault/assets/{id}";
            if (forceAdmin) url += "?forceAdmin=true";
            
            var response = await _httpClient.DeleteAsync(url);
            
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                throw new UnauthorizedAccessException("FORBIDDEN_NOT_OWNER");
            }
            
            response.EnsureSuccessStatusCode();
        }

        public async Task<int> CleanOrphanedAssetsAsync()
        {
            var response = await _httpClient.DeleteAsync("/api/v1/vault/maintenance/orphans");
            response.EnsureSuccessStatusCode();
            
            var result = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            return result.GetProperty("deletedCount").GetInt32();
        }

        public async Task RestoreAssetAsync(Guid id)
        {
            var response = await _httpClient.PutAsync($"/api/v1/vault/assets/{id}/restore", null);
            response.EnsureSuccessStatusCode();
        }

        public async Task<bool> AppendAuditLogAsync(Guid assetId, AuditLogRequestDto logRequest)
        {
            var response = await _httpClient.PostAsJsonAsync($"/api/v1/vault/assets/{assetId}/audit", logRequest);
            return response.IsSuccessStatusCode;
        }

        public async Task<byte[]> GetLatestAuditHashAsync(Guid assetId)
        {
            var response = await _httpClient.GetAsync($"/api/v1/vault/assets/{assetId}/audit/latest-hash");
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                var hashStr = result.GetProperty("hash").GetString();
                if (!string.IsNullOrEmpty(hashStr))
                {
                    return Convert.FromBase64String(hashStr);
                }
            }
            return new byte[32]; // Fallback genesis hash
        }

        public async Task SetupRecoveryAsync(SetupRecoveryDto request)
        {
            var response = await _httpClient.PostAsJsonAsync("/api/v1/recovery/setup", request);
            response.EnsureSuccessStatusCode();
        }

        public async Task RequestRecoveryAsync(InitiateRecoveryRequestDto request)
        {
            var response = await _httpClient.PostAsJsonAsync("/api/v1/recovery/request", request);
            response.EnsureSuccessStatusCode();
        }

        public async Task ApproveRecoveryAsync(ProvideRecoveryShareDto request)
        {
            var response = await _httpClient.PostAsJsonAsync("/api/v1/recovery/approve", request);
            response.EnsureSuccessStatusCode();
        }

        public async Task<RecoveryStatusResponseDto?> GetRecoveryStatusAsync(string adSid)
        {
            var response = await _httpClient.GetAsync($"/api/v1/recovery/status/{adSid}");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<RecoveryStatusResponseDto>();
        }
    }
}
