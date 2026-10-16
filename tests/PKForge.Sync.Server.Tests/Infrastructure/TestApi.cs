using System.Net.Http.Json;
using PKForge.Sync.Protocol;

namespace PKForge.Sync.Server.Tests.Infrastructure;

public static class TestApi
{
    public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PostAsJsonAsync(url, body, ProtocolJson.Options);

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(ProtocolJson.Options);
        return value ?? throw new InvalidOperationException("Empty response body.");
    }

    public static Task<ErrorResponse> ReadErrorAsync(this HttpResponseMessage response) => response.ReadAsync<ErrorResponse>();

    public const string Password = "pikachu123";

    public static async Task<(HttpClient Client, DeviceKeyResponse Key)> RegisterAsync(
        this SyncServerFactory server, string email = "ash@example.com", string device = "Phone")
    {
        var response = await server.Client().PostJsonAsync("/v1/accounts", new RegisterRequest(email, Password, device));
        response.EnsureSuccessStatusCode();
        var key = await response.ReadAsync<DeviceKeyResponse>();
        return (server.AuthedClient(key.DeviceKey), key);
    }

    public static HttpClient AuthedClient(this SyncServerFactory server, string deviceKey)
    {
        var client = server.Client();
        client.DefaultRequestHeaders.Authorization = new("Bearer", deviceKey);
        return client;
    }
}
