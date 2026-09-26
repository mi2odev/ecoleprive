using CentreSoutien.Application.Abstractions;
using CentreSoutien.Infrastructure.Security;

namespace CentreSoutien.Infrastructure.Storage;

public sealed class SystemInfo(AppPaths paths, StorageOptions options, IKeyProtector protector) : ISystemInfo
{
    public bool DatabaseEncrypted => options.EncryptDatabase;
    public string KeyProtection => protector.Name == "dpapi" ? "Windows (DPAPI, compte utilisateur)" : "Aucune (clé en clair)";
    public string DataFolder => paths.Root;
    public string DatabasePath => paths.Database;
}
