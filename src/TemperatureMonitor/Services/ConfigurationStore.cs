using System.Text.Json;
using TemperatureMonitor.Models;

namespace TemperatureMonitor.Services;

/// <summary>Reads and writes the sensor configuration file (sensors.json).</summary>
public static class ConfigurationStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    /// <summary>Parses a JSON array of sensor configurations. Throws <see cref="JsonException"/> on malformed JSON.</summary>
    public static List<SensorConfig> ParseJson(string json)
    {
        var configs = JsonSerializer.Deserialize<List<SensorConfig?>>(json, Options);
        return configs?.Select(c => c ?? new SensorConfig()).ToList() ?? new List<SensorConfig>();
    }

    public static List<SensorConfig> Load(string path) => ParseJson(File.ReadAllText(path));

    public static string ToJson(IEnumerable<SensorConfig> configs) => JsonSerializer.Serialize(configs, Options);

    public static void Save(string path, IEnumerable<SensorConfig> configs)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, ToJson(configs));
    }
}
