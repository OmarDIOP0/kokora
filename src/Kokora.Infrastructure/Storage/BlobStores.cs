using Kokora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kokora.Infrastructure.Storage;

/// <summary>
/// Stockage des fichiers téléversés (photos, logos) : sur disque (serveur, Docker) ou dans PostgreSQL
/// (hébergeurs sans disque permanent, comme Render gratuit). Clés de la forme « logos/asc-soum-1a2b3c4d.webp ».
/// </summary>
public interface IBlobStore
{
    Task WriteAsync(string key, byte[] data, string contentType, CancellationToken ct = default);
    Task<(byte[] Data, string ContentType)?> ReadAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}

/// <summary>Fichiers dans wwwroot/uploads (ou Storage:UploadsPath), servis directement par le serveur web.</summary>
public class LocalBlobStore(string root) : IBlobStore
{
    private string PathOf(string key)
    {
        var full = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Chemin de fichier invalide.");
        return full;
    }

    public async Task WriteAsync(string key, byte[] data, string contentType, CancellationToken ct = default)
    {
        var path = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, data, ct);
    }

    public async Task<(byte[] Data, string ContentType)?> ReadAsync(string key, CancellationToken ct = default)
    {
        var path = PathOf(key);
        return File.Exists(path) ? (await File.ReadAllBytesAsync(path, ct), "application/octet-stream") : null;
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = PathOf(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }
}

/// <summary>Fichier stocké en base (mode Storage:Mode = Database).</summary>
public class StoredFile
{
    public string Key { get; set; } = "";
    public string ContentType { get; set; } = "";
    public byte[] Data { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Fichiers dans la table stored_files : aucun disque nécessaire, sauvegardés avec la base.</summary>
public class DatabaseBlobStore(IServiceScopeFactory scopes) : IBlobStore
{
    public async Task WriteAsync(string key, byte[] data, string contentType, CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var file = await db.StoredFiles.FindAsync([key], ct);
        if (file is null) db.StoredFiles.Add(file = new StoredFile { Key = key });
        file.Data = data;
        file.ContentType = contentType;
        await db.SaveChangesAsync(ct);
    }

    public async Task<(byte[] Data, string ContentType)?> ReadAsync(string key, CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var file = await db.StoredFiles.AsNoTracking().Where(f => f.Key == key)
            .Select(f => new { f.Data, f.ContentType }).FirstOrDefaultAsync(ct);
        return file is null ? null : (file.Data, file.ContentType);
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().StoredFiles.Where(f => f.Key == key).ExecuteDeleteAsync(ct);
    }
}
