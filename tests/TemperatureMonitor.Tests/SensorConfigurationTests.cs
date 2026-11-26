using TemperatureMonitor.Models;
using TemperatureMonitor.Services;
using static TemperatureMonitor.Tests.TestHelpers;

namespace TemperatureMonitor.Tests;

public class SensorConfigurationTests
{
    [Fact]
    public void Validate_ValidConfig_IsValid()
    {
        Assert.True(SensorConfigValidator.Validate(Config()).IsValid);
    }

    [Fact]
    public void Validate_NullConfig_IsInvalid()
    {
        Assert.False(SensorConfigValidator.Validate(null).IsValid);
    }

    [Theory]
    [InlineData(null, "Rack", "Row")]
    [InlineData("", "Rack", "Row")]
    [InlineData("S1", " ", "Row")]
    [InlineData("S1", "Rack", null)]
    public void Validate_MissingRequiredField_IsInvalid(string? id, string? name, string? location)
    {
        var result = SensorConfigValidator.Validate(Config(id, name, location));
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(60.0, -10.0)] // min above max
    [InlineData(20.0, 20.0)] // min equal to max
    public void Validate_MinNotBelowMax_IsInvalid(double min, double max)
    {
        var result = SensorConfigValidator.Validate(Config(min: min, max: max, warning: 20, critical: 20.5));
        Assert.Contains(result.Errors, e => e.Contains("Minimum temperature"));
    }

    [Theory]
    [InlineData(30.0, 26.0)] // warning above critical
    [InlineData(28.0, 28.0)] // warning equal to critical
    public void Validate_WarningNotBelowCritical_IsInvalid(double warning, double critical)
    {
        var result = SensorConfigValidator.Validate(Config(warning: warning, critical: critical));
        Assert.Contains(result.Errors, e => e.Contains("Warning threshold") && e.Contains("lower than critical"));
    }

    [Theory]
    [InlineData(-20.0, 30.0)] // warning below min
    [InlineData(26.0, 70.0)] // critical above max
    public void Validate_ThresholdOutsideLimits_IsInvalid(double warning, double critical)
    {
        Assert.False(SensorConfigValidator.Validate(Config(warning: warning, critical: critical)).IsValid);
    }

    [Fact]
    public void Validate_NaNLimit_IsInvalid()
    {
        Assert.False(SensorConfigValidator.Validate(Config(max: double.NaN)).IsValid);
    }

    [Fact]
    public void ValidateThresholds_InfiniteValue_IsInvalid()
    {
        Assert.False(SensorConfigValidator.ValidateThresholds(Config(), 26, double.PositiveInfinity).IsValid);
    }

    [Fact]
    public void InitialiseSensor_MixOfValidAndInvalid_RegistersOnlyValid()
    {
        using var f = new ManagerFixture(registerDefaults: false);

        var result = f.Manager.InitialiseSensor(new[]
        {
            Config("S1"),
            Config("", "No ID"),
            Config("S3", min: 60, max: -10),
            Config("S4")
        });

        Assert.Equal(2, result.Loaded.Count);
        Assert.Equal(2, result.Rejected.Count);
        Assert.True(f.Manager.IsRegistered("S1"));
        Assert.True(f.Manager.IsRegistered("S4"));
        Assert.False(f.Manager.IsRegistered("S3"));
        Assert.Contains(f.Logger.Lines, l => l.Contains("S3 rejected"));
    }

    [Fact]
    public void InitialiseSensor_InvalidSensorDoesNotStopValidSensorFromRunning()
    {
        using var f = new ManagerFixture(registerDefaults: false);
        f.Manager.InitialiseSensor(new[] { Config("BAD", min: 50, max: 0), Config("GOOD") });

        Assert.True(f.Manager.StartSensor("GOOD"));
        Assert.True(WaitUntil(() => f.Manager.GetHistory("GOOD").Count > 0));
        Assert.False(f.Manager.StartSensor("BAD"));
    }

    [Fact]
    public void InitialiseSensor_DuplicateId_SecondIsRejected()
    {
        using var f = new ManagerFixture(registerDefaults: false);

        var result = f.Manager.InitialiseSensor(new[] { Config("S1", "First"), Config("S1", "Second") });

        Assert.Single(result.Loaded);
        Assert.Single(result.Rejected);
        Assert.Contains("Duplicate", result.Rejected[0].Errors[0]);
        Assert.Equal("First", f.Manager.GetSensors().Single().Name);
    }

    [Fact]
    public void InitialiseSensor_NullEntry_IsRejected()
    {
        using var f = new ManagerFixture(registerDefaults: false);
        var result = f.Manager.InitialiseSensor(new SensorConfig?[] { null, Config("S1") });
        Assert.Single(result.Loaded);
        Assert.Single(result.Rejected);
    }

    [Fact]
    public void InitialiseSensorsFromJson_ValidJson_LoadsSensors()
    {
        using var f = new ManagerFixture(registerDefaults: false);
        const string json = """
            [
              { "id": "A1", "name": "Aisle", "location": "Row 9", "minTemp": 0, "maxTemp": 50, "warningThreshold": 25, "criticalThreshold": 30 },
              { "id": "A2", "name": "Broken", "location": "Row 9", "minTemp": 0, "maxTemp": 50, "warningThreshold": 35, "criticalThreshold": 30 }
            ]
            """;

        var result = f.Manager.InitialiseSensorsFromJson(json);

        Assert.Single(result.Loaded);
        Assert.Equal("A1", result.Loaded[0].Id);
    }

    [Fact]
    public void InitialiseSensorsFromJson_MalformedJson_LoadsNothingAndLogsError()
    {
        using var f = new ManagerFixture(registerDefaults: false);

        var result = f.Manager.InitialiseSensorsFromJson("{ this is not json");

        Assert.Empty(result.Loaded);
        Assert.Contains(f.Logger.Lines, l => l.Contains("not valid JSON"));
    }

    [Fact]
    public void InitialiseSensorsFromFile_MissingFile_LoadsNothingAndLogsError()
    {
        using var f = new ManagerFixture(registerDefaults: false);

        var result = f.Manager.InitialiseSensorsFromFile(TempPath("missing.json"));

        Assert.Empty(result.Loaded);
        Assert.Contains(f.Logger.Lines, l => l.Contains("not found"));
    }

    [Fact]
    public void InitialiseSensorsFromFile_ExistingFile_LoadsSensors()
    {
        string path = TempPath("sensors.json");
        ConfigurationStore.Save(path, new[] { Config("F1"), Config("F2") });
        using var f = new ManagerFixture(registerDefaults: false);

        var result = f.Manager.InitialiseSensorsFromFile(path);

        Assert.Equal(2, result.Loaded.Count);
    }
}
