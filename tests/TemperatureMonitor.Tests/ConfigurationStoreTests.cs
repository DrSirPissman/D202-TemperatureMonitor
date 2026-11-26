using System.Text.Json;
using TemperatureMonitor.Services;
using static TemperatureMonitor.Tests.TestHelpers;

namespace TemperatureMonitor.Tests;

public class ConfigurationStoreTests
{
    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        string path = TempPath("sensors.json");
        var original = Config("S7", "Cold Aisle", "Row 7", -5, 55, 25, 29);

        ConfigurationStore.Save(path, new[] { original });
        var loaded = ConfigurationStore.Load(path).Single();

        Assert.Equal("S7", loaded.Id);
        Assert.Equal("Cold Aisle", loaded.Name);
        Assert.Equal("Row 7", loaded.Location);
        Assert.Equal(-5, loaded.MinTemp);
        Assert.Equal(55, loaded.MaxTemp);
        Assert.Equal(25, loaded.WarningThreshold);
        Assert.Equal(29, loaded.CriticalThreshold);
    }

    [Fact]
    public void ParseJson_IsCaseInsensitive()
    {
        var configs = ConfigurationStore.ParseJson("""[{ "ID": "X", "NAME": "n", "location": "l" }]""");
        Assert.Equal("X", configs.Single().Id);
    }

    [Fact]
    public void ParseJson_NullEntriesBecomeEmptyConfigs()
    {
        var configs = ConfigurationStore.ParseJson("[null]");
        Assert.Single(configs);
        Assert.Null(configs[0].Id);
    }

    [Fact]
    public void ParseJson_Malformed_Throws()
    {
        Assert.ThrowsAny<JsonException>(() => ConfigurationStore.ParseJson("not json"));
    }

    [Fact]
    public void ParseJson_JsonNull_ReturnsEmptyList()
    {
        Assert.Empty(ConfigurationStore.ParseJson("null"));
    }

    [Fact]
    public void SaveConfiguration_FromManager_WritesRegisteredSensors()
    {
        using var f = new ManagerFixture();
        string path = TempPath("sensors.json");

        f.Manager.SaveConfiguration(path);

        Assert.Equal(new[] { "S1", "S2" }, ConfigurationStore.Load(path).Select(c => c.Id));
    }

    [Fact]
    public void SensorReading_ToString_ContainsKeyFields()
    {
        var text = Reading(23.456, Config("S1", "Rack A", "Row 1")).ToString();
        Assert.Contains("S1 (Rack A, Row 1)", text);
        Assert.Contains("23.46", text);
        Assert.Contains("Normal", text);
        Assert.Contains("NotEvaluated", text);
    }
}
