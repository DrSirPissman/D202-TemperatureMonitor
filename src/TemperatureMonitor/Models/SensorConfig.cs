namespace TemperatureMonitor.Models;

/// <summary>
/// Configuration for one temperature sensor, loaded from sensors.json.
/// </summary>
public class SensorConfig
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Location { get; set; }

    /// <summary>Lowest temperature (°C) the sensor can physically report. Readings below this are rejected.</summary>
    public double MinTemp { get; set; }

    /// <summary>Highest temperature (°C) the sensor can physically report. Readings above this are rejected.</summary>
    public double MaxTemp { get; set; }

    /// <summary>Readings at or above this value (°C) are given Warning status.</summary>
    public double WarningThreshold { get; set; }

    /// <summary>Readings at or above this value (°C) are given Critical status.</summary>
    public double CriticalThreshold { get; set; }

    public SensorConfig Clone() => (SensorConfig)MemberwiseClone();
}
