using CentreSoutien.Application.Abstractions;

namespace CentreSoutien.Infrastructure.Storage;

public sealed class FileStorage(AppPaths paths) : IFileStorage
{
    public string RootFolder => paths.Root;

    public string Import(string sourcePath, string area)
    {
        if (!File.Exists(sourcePath)) throw new BusinessException("Fichier introuvable : " + sourcePath);
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        var name = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..28] + ext;
        File.Copy(sourcePath, Path.Combine(paths.Area(area), name), overwrite: false);
        return name;
    }

    public string GetPath(string storedFile, string area)
    {
        // Stored names never contain directories; guard against path traversal from a tampered database.
        var safe = Path.GetFileName(storedFile);
        return Path.Combine(paths.Area(area), safe);
    }

    public void Delete(string storedFile, string area)
    {
        var p = GetPath(storedFile, area);
        if (File.Exists(p)) File.Delete(p);
    }
}
