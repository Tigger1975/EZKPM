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
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            return result.GetProperty("assetId").GetGuid();
        }

        public async Task<bool> AppendAuditLogAsync(Guid assetId, AuditLogRequestDto logRequest)
        {
            var response = await _httpClient.PostAsJsonAsync($"/api/v1/vault/assets/{assetId}/audit", logRequest);
            return response.IsSuccessStatusCode;
        }
    }
}
