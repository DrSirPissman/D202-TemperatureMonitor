using TemperatureMonitor.Models;

namespace TemperatureMonitor.Services;

/// <summary>
/// The temperature-processing rules: reading validation, threshold checks, anomaly detection and smoothing.
/// All methods are pure (no side effects) so they can be tested directly.
/// </summary>
public static class TemperatureProcessor
{
    /// <summary>Number of previous readings used for anomaly detection and smoothing.</summary>
    public const int WindowSize = 5;

    /// <summary>A change of at least this many °C from the recent average is an anomaly.</summary>
    public const double AnomalyDifference = 3.0;

    /// <summary>
    /// ValidateData: checks required fields and that the temperature is within the sensor's limits (inclusive).
    /// </summary>
    public static ValidationResult ValidateData(SensorReading? reading, SensorConfig config)
    {
        var result = new ValidationResult();

        if (reading == null)
        {
            result.Errors.Add("Reading is missing.");
            return result;
        }

        if (string.IsNullOrWhiteSpace(reading.SensorId))
            result.Errors.Add("Reading has no sensor ID.");
        if (string.IsNullOrWhiteSpace(reading.SensorName))
            result.Errors.Add("Reading has no sensor name.");
        if (string.IsNullOrWhiteSpace(reading.Location))
            result.Errors.Add("Reading has no location.");
        if (reading.Timestamp == default)
            result.Errors.Add("Reading has no timestamp.");

        if (double.IsNaN(reading.Temperature) || double.IsInfinity(reading.Temperature))
        {
            result.Errors.Add("Temperature is not a real number.");
        }
        else if (reading.Temperature < config.MinTemp || reading.Temperature > config.MaxTemp)
        {
            result.Errors.Add(
                $"Temperature {reading.Temperature:F2} °C is outside the sensor limits ({config.MinTemp} to {config.MaxTemp} °C).");
        }

        return result;
    }

    /// <summary>
    /// CheckThreshold: Critical if temperature &gt;= critical threshold, Warning if &gt;= warning threshold, otherwise Normal.
    /// </summary>
    public static ThresholdStatus CheckThreshold(double temperature, SensorConfig config)
    {
        if (temperature >= config.CriticalThreshold)
            return ThresholdStatus.Critical;
        if (temperature >= config.WarningThreshold)
            return ThresholdStatus.Warning;
        return ThresholdStatus.Normal;
    }

    /// <summary>
    /// DetectAnomaly: compares the latest valid reading with the average of the previous five valid readings
    /// from the same sensor. A difference of at least 3 °C is an anomaly. Returns NotEvaluated when fewer than
    /// five previous readings exist.
    /// </summary>
    /// <param name="latest">The new valid temperature.</param>
    /// <param name="previousReadings">Earlier valid temperatures for the same sensor, oldest first.</param>
    public static AnomalyStatus DetectAnomaly(double latest, IReadOnlyList<double> previousReadings)
    {
        if (previousReadings.Count < WindowSize)
            return AnomalyStatus.NotEvaluated;

        double average = previousReadings.Skip(previousReadings.Count - WindowSize).Average();
        double difference = Math.Abs(latest - average);

        // Round to avoid floating-point noise turning exactly 3.0 into 2.9999999.
        return Math.Round(difference, 6) >= AnomalyDifference ? AnomalyStatus.Anomaly : AnomalyStatus.Normal;
    }

    /// <summary>
    /// SmoothData: average of the latest five valid readings, or of all readings if fewer than five.
    /// Returns null when there are no readings.
    /// </summary>
    public static double? SmoothData(IReadOnlyList<double> readings)
    {
        if (readings.Count == 0)
            return null;

        int take = Math.Min(WindowSize, readings.Count);
        return readings.Skip(readings.Count - take).Average();
    }
}
