using System.Text.Json;
using PKForge.Sync.Protocol;

namespace PKForge.Sync.Server.Tests.Infrastructure;

public static class Mons
{
    /// <summary>Stand-in Pokémon bytes (the server never parses them).</summary>
    public static byte[] Bytes(int seed)
    {
        var bytes = new byte[344];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    public static async Task<string> UploadAsync(this HttpClient client, byte[] data)
    {
        var hash = BlobHash.Compute(data);
        (await client.PutAsync($"/v1/blobs/{hash}", new ByteArrayContent(data))).EnsureSuccessStatusCode();
        return hash;
    }

    public static readonly DateTimeOffset T0 = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    public static ItemWrite Write(Guid id, int box, int slot, string blob, long baseVersion = 0, string nickname = "Pikachu") =>
        new(ItemKinds.Mon, id.ToString(), baseVersion, JsonSerializer.SerializeToElement(
            new MonValue(box, slot, T0, JsonSerializer.SerializeToElement(new { species = 25, nickname }), T0, blob), ProtocolJson.Options));

    public static ItemWrite Delete(Guid id, long baseVersion) => new(ItemKinds.Mon, id.ToString(), baseVersion, null, Deleted: true);

    public static ItemWrite Boxes(int count, long baseVersion = 0) =>
        new(ItemKinds.Boxes, ItemKinds.SingletonId, baseVersion, JsonSerializer.SerializeToElement(new BoxesValue(count, [], []), ProtocolJson.Options));

    public static ChangeGroup Group(params ItemWrite[] writes) => new(Guid.NewGuid(), writes);

    public static async Task<PushResponse> PushAsync(this HttpClient client, params ChangeGroup[] groups)
    {
        var response = await client.PostJsonAsync("/v1/sync/push", new PushRequest(groups));
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<PushResponse>();
    }

    public static async Task<ChangesResponse> ChangesAsync(this HttpClient client, long since, int? limit = null)
    {
        var response = await client.GetAsync($"/v1/sync/changes?since={since}" + (limit is null ? "" : $"&limit={limit}"));
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<ChangesResponse>();
    }
}
