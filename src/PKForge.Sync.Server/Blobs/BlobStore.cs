namespace PKForge.Sync.Server.Blobs;

/// <summary>Where Pokémon bytes live. The database records which blobs an account has.</summary>
public interface IBlobStore
{
    Task PutAsync(Guid accountId, string hash, byte[] data, CancellationToken ct);
    Task<byte[]?> GetAsync(Guid accountId, string hash, CancellationToken ct);
    Task<bool> ExistsAsync(Guid accountId, string hash, CancellationToken ct);
    Task DeleteAsync(Guid accountId, string hash, CancellationToken ct);
    Task DeleteAllAsync(Guid accountId, CancellationToken ct);
}

public sealed class FileSystemBlobStore(string root) : IBlobStore
{
    public async Task PutAsync(Guid accountId, string hash, byte[] data, CancellationToken ct)
    {
        var path = PathOf(accountId, hash);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllBytesAsync(temp, data, ct);
        File.Move(temp, path, overwrite: true);
    }

    public async Task<byte[]?> GetAsync(Guid accountId, string hash, CancellationToken ct)
    {
        var path = PathOf(accountId, hash);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path, ct) : null;
    }

    public Task<bool> ExistsAsync(Guid accountId, string hash, CancellationToken ct) => Task.FromResult(File.Exists(PathOf(accountId, hash)));

    public Task DeleteAsync(Guid accountId, string hash, CancellationToken ct)
    {
        var path = PathOf(accountId, hash);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public Task DeleteAllAsync(Guid accountId, CancellationToken ct)
    {
        var directory = Path.Combine(root, accountId.ToString("N"));
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        return Task.CompletedTask;
    }

    private string PathOf(Guid accountId, string hash) => Path.Combine(root, accountId.ToString("N"), hash[..2], hash);
}
