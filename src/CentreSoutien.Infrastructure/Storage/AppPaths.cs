using CentreSoutien.Application.Abstractions;

namespace CentreSoutien.Infrastructure.Storage;

/// <summary>Bound from the "Storage" section of appsettings.json.</summary>
public sealed class StorageOptions
{
    /// <summary>Root data folder. Empty = %LOCALAPPDATA%\CentreSoutien.</summary>
    public string? DataFolder { get; set; }
    public string DatabaseFile { get; set; } = "centre.db";
    /// <summary>Encrypt the SQLite database with SQLCipher (key protected by Windows DPAPI).</summary>
    public bool EncryptDatabase { get; set; } = true;
}

public sealed class AppPaths
{
    public AppPaths(StorageOptions options)
    {
        Root = string.IsNullOrWhiteSpace(options.DataFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CentreSoutien")
            : Environment.ExpandEnvironmentVariables(options.DataFolder);
        Directory.CreateDirectory(Root);
        Database = Path.Combine(Root, options.DatabaseFile);
        KeyFile = Path.Combine(Root, "keys.json");
        Images = Path.Combine(Root, "Images");
        Documents = Path.Combine(Root, "Documents");
        Backups = Path.Combine(Root, "Backups");
        Logs = Path.Combine(Root, "Logs");
        foreach (var d in new[] { Images, Documents, Backups, Logs }) Directory.CreateDirectory(d);
    }

    public string Root { get; }
    public string Database { get; }
    public string KeyFile { get; }
    public string Images { get; }
    public string Documents { get; }
    public string Backups { get; }
    public string Logs { get; }

    public string Area(string area) => area switch
    {
        StorageAreas.Images => Images,
        StorageAreas.Documents => Documents,
        _ => throw new ArgumentOutOfRangeException(nameof(area)),
    };
}
