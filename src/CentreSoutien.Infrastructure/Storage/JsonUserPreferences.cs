using System.Text.Json;
using System.Text.Json.Nodes;
using CentreSoutien.Application.Abstractions;

namespace CentreSoutien.Infrastructure.Storage;

/// <summary>Preferences stored in preferences.json in the data folder. Corrupt or missing files fall back to defaults.</summary>
public sealed class JsonUserPreferences(AppPaths paths) : IUserPreferences
{
    private readonly object _gate = new();
    private JsonObject? _data;
    private string File => Path.Combine(paths.Root, "preferences.json");

    public T Get<T>(string key, T defaultValue)
    {
        lock (_gate)
        {
            var node = Load()[key];
            if (node is null) return defaultValue;
            try
            {
                return node.Deserialize<T>() ?? defaultValue;
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
            {
                return defaultValue;
            }
        }
    }

    public void Set<T>(string key, T value)
    {
        lock (_gate)
        {
            var data = Load();
            data[key] = JsonSerializer.SerializeToNode(value);
            var tmp = File + ".tmp";
            System.IO.File.WriteAllText(tmp, data.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            System.IO.File.Move(tmp, File, overwrite: true);
        }
    }

    private JsonObject Load()
    {
        if (_data is not null) return _data;
        try
        {
            _data = System.IO.File.Exists(File) ? JsonNode.Parse(System.IO.File.ReadAllText(File)) as JsonObject : null;
        }
        catch (JsonException)
        {
            _data = null;
        }
        return _data ??= new JsonObject();
    }
}
