using TemperatureMonitor.Models;

namespace TemperatureMonitor.Tests;

/// <summary>Shared builders and fakes for the tests.</summary>
public static class TestHelpers
{
    public static SensorConfig Config(
        string? id = "S1",
        string? name = "Rack A",
        string? location = "Row 1",
        double min = -10,
        double max = 60,
        double warning = 26,
        double critical = 30) => new()
    {
        Id = id,
        Name = name,
        Location = location,
        MinTemp = min,
        MaxTemp = max,
        WarningThreshold = warning,
        CriticalThreshold = critical
    };

    public static SensorReading Reading(double temperature, SensorConfig? config = null)
    {
        config ??= Config();
        return new SensorReading
        {
            SensorId = config.Id,
            SensorName = config.Name,
            Location = config.Location,
            Temperature = temperature,
            Timestamp = new DateTime(2026, 11, 26, 12, 0, 0)
        };
    }

    /// <summary>Waits until a condition becomes true or the timeout passes.</summary>
    public static bool WaitUntil(Func<bool> condition, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            Thread.Sleep(10);
        }
        return condition();
    }

    public static string TempPath(string fileName) =>
        Path.Combine(Path.GetTempPath(), "TemperatureMonitorTests", Guid.NewGuid().ToString("N"), fileName);
}
