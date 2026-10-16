using System.Net;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Tests.Infrastructure;

namespace PKForge.Sync.Server.Tests;

public sealed class BlobTests
{
    [Fact]
    public async Task UploadedBlobsAreKnownAndDownloadable()
    {
        using var server = new SyncServerFactory();
        var (client, key) = await server.RegisterAsync();
        var data = Mons.Bytes(1);
        var other = BlobHash.Compute(Mons.Bytes(2));

        var hash = await client.UploadAsync(data);
        var missing = await (await client.PostJsonAsync("/v1/blobs/missing", new MissingBlobsRequest([hash, other]))).ReadAsync<MissingBlobsResponse>();
        var downloaded = await client.GetByteArrayAsync($"/v1/blobs/{hash}");

        Assert.Equal([other], missing.Missing);
        Assert.Equal(data, downloaded);
        Assert.True(File.Exists(Path.Combine(server.DataDirectory, "blobs", key.AccountId.ToString("N"), hash[..2], hash)));
    }

    [Fact]
    public async Task UploadingTwiceIsFine()
    {
        using var server = new SyncServerFactory();
        var (client, _) = await server.RegisterAsync();
        await client.UploadAsync(Mons.Bytes(1));
        await client.UploadAsync(Mons.Bytes(1));
    }

    [Fact]
    public async Task BytesMustMatchTheirId()
    {
        using var server = new SyncServerFactory();
        var (client, _) = await server.RegisterAsync();

        var response = await client.PutAsync($"/v1/blobs/{BlobHash.Compute(Mons.Bytes(1))}", new ByteArrayContent(Mons.Bytes(2)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.BlobHashMismatch, (await response.ReadErrorAsync()).Code);
    }

    [Fact]
    public async Task ABlobWhoseFileVanishedIsReportedMissingAndCanBeUploadedAgain()
    {
        using var server = new SyncServerFactory();
        var (client, key) = await server.RegisterAsync();
        var data = Mons.Bytes(1);
        var hash = await client.UploadAsync(data);
        File.Delete(Path.Combine(server.DataDirectory, "blobs", key.AccountId.ToString("N"), hash[..2], hash));

        var missing = await (await client.PostJsonAsync("/v1/blobs/missing", new MissingBlobsRequest([hash]))).ReadAsync<MissingBlobsResponse>();
        await client.UploadAsync(data);

        Assert.Equal([hash], missing.Missing);
        Assert.Equal(data, await client.GetByteArrayAsync($"/v1/blobs/{hash}"));
    }

    [Fact]
    public async Task OversizedBlobsAreRefused()
    {
        using var server = new SyncServerFactory();
        server.Settings["Sync:MaxBlobBytes"] = "100";
        var (client, _) = await server.RegisterAsync();
        var data = Mons.Bytes(1);

        var response = await client.PutAsync($"/v1/blobs/{BlobHash.Compute(data)}", new ByteArrayContent(data));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(ErrorCodes.BlobTooLarge, (await response.ReadErrorAsync()).Code);
    }

    [Theory]
    [InlineData("ABCDEF")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000ZZZ")]
    public async Task BadBlobIdsAreRefused(string hash)
    {
        using var server = new SyncServerFactory();
        var (client, _) = await server.RegisterAsync();

        var put = await client.PutAsync($"/v1/blobs/{hash}", new ByteArrayContent([1, 2, 3]));
        var missing = await client.PostJsonAsync("/v1/blobs/missing", new MissingBlobsRequest([hash]));

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    [Fact]
    public async Task AccountsCannotSeeEachOthersBlobs()
    {
        using var server = new SyncServerFactory();
        var (ash, _) = await server.RegisterAsync("ash@example.com");
        var (misty, _) = await server.RegisterAsync("misty@example.com");
        var hash = await ash.UploadAsync(Mons.Bytes(1));

        var download = await misty.GetAsync($"/v1/blobs/{hash}");
        var missing = await (await misty.PostJsonAsync("/v1/blobs/missing", new MissingBlobsRequest([hash]))).ReadAsync<MissingBlobsResponse>();

        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
        Assert.Equal([hash], missing.Missing);
    }

    [Fact]
    public async Task BatchUploadStoresEveryBlobAndBatchDownloadReturnsThem()
    {
        using var server = new SyncServerFactory();
        var (client, _) = await server.RegisterAsync();
        var blobs = Enumerable.Range(1, 3).Select(Mons.Bytes).Select(b => new BlobData(BlobHash.Compute(b), b)).ToList();
        var unknown = BlobHash.Compute(Mons.Bytes(99));

        var upload = await client.PostJsonAsync("/v1/blobs/upload", new UploadBlobsRequest(blobs));
        var download = await (await client.PostJsonAsync("/v1/blobs/download",
            new DownloadBlobsRequest([.. blobs.Select(b => b.Hash), unknown]))).ReadAsync<DownloadBlobsResponse>();

        Assert.Equal(HttpStatusCode.NoContent, upload.StatusCode);
        Assert.Equal(blobs.Select(b => b.Hash).Order(), download.Blobs.Select(b => b.Hash).Order());
        Assert.All(download.Blobs, b => Assert.Equal(b.Hash, BlobHash.Compute(b.Data)));
    }

    [Fact]
    public async Task ABadBlobRejectsTheWholeBatch()
    {
        using var server = new SyncServerFactory();
        var (client, _) = await server.RegisterAsync();
        var good = new BlobData(BlobHash.Compute(Mons.Bytes(1)), Mons.Bytes(1));
        var bad = new BlobData(BlobHash.Compute(Mons.Bytes(2)), Mons.Bytes(3));

        var upload = await client.PostJsonAsync("/v1/blobs/upload", new UploadBlobsRequest([good, bad]));
        var missing = await (await client.PostJsonAsync("/v1/blobs/missing", new MissingBlobsRequest([good.Hash]))).ReadAsync<MissingBlobsResponse>();

        Assert.Equal(HttpStatusCode.BadRequest, upload.StatusCode);
        Assert.Equal(ErrorCodes.BlobHashMismatch, (await upload.ReadErrorAsync()).Code);
        Assert.Equal([good.Hash], missing.Missing);
    }

    [Fact]
    public async Task BatchDownloadOnlySeesTheAccountsOwnBlobs()
    {
        using var server = new SyncServerFactory();
        var (ash, _) = await server.RegisterAsync("ash@example.com");
        var (misty, _) = await server.RegisterAsync("misty@example.com");
        var hash = await ash.UploadAsync(Mons.Bytes(1));

        var download = await (await misty.PostJsonAsync("/v1/blobs/download", new DownloadBlobsRequest([hash]))).ReadAsync<DownloadBlobsResponse>();

        Assert.Empty(download.Blobs);
    }
}
