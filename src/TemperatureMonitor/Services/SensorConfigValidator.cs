using TemperatureMonitor.Models;

namespace TemperatureMonitor.Services;

/// <summary>
/// Checks a sensor configuration for required fields, sensible temperature limits and thresholds.
/// </summary>
public static class SensorConfigValidator
{
    public static ValidationResult Validate(SensorConfig? config)
    {
        var result = new ValidationResult();

        if (config == null)
        {
            result.Errors.Add("Configuration is missing.");
            return result;
        }

        if (string.IsNullOrWhiteSpace(config.Id))
            result.Errors.Add("Sensor ID is required.");
        if (string.IsNullOrWhiteSpace(config.Name))
            result.Errors.Add("Sensor name is required.");
        if (string.IsNullOrWhiteSpace(config.Location))
            result.Errors.Add("Sensor location is required.");

        if (!IsFinite(config.MinTemp) || !IsFinite(config.MaxTemp) ||
            !IsFinite(config.WarningThreshold) || !IsFinite(config.CriticalThreshold))
        {
            result.Errors.Add("Temperature limits and thresholds must be real numbers.");
            return result;
        }

        if (config.MinTemp >= config.MaxTemp)
            result.Errors.Add($"Minimum temperature ({config.MinTemp}) must be lower than maximum temperature ({config.MaxTemp}).");

        result.Errors.AddRange(ValidateThresholds(config, config.WarningThreshold, config.CriticalThreshold).Errors);

        return result;
    }

    /// <summary>
    /// Checks a warning/critical pair against a sensor's limits. Used both when loading and when the user edits thresholds.
    /// </summary>
    public static ValidationResult ValidateThresholds(SensorConfig config, double warning, double critical)
    {
        var result = new ValidationResult();

        if (!IsFinite(warning) || !IsFinite(critical))
        {
            result.Errors.Add("Thresholds must be real numbers.");
            return result;
        }

        if (warning >= critical)
            result.Errors.Add($"Warning threshold ({warning}) must be lower than critical threshold ({critical}).");

        if (warning < config.MinTemp || warning > config.MaxTemp)
            result.Errors.Add($"Warning threshold ({warning}) must be between {config.MinTemp} and {config.MaxTemp}.");

        if (critical < config.MinTemp || critical > config.MaxTemp)
            result.Errors.Add($"Critical threshold ({critical}) must be between {config.MinTemp} and {config.MaxTemp}.");

        return result;
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
