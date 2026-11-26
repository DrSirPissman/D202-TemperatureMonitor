namespace TemperatureMonitor.Models;

/// <summary>Result of comparing a reading with the sensor's warning and critical thresholds.</summary>
public enum ThresholdStatus
{
    Normal,
    Warning,
    Critical
}

/// <summary>Result of comparing a reading with the sensor's recent history.</summary>
public enum AnomalyStatus
{
    /// <summary>Fewer than five previous valid readings exist, so no comparison was made.</summary>
    NotEvaluated,
    Normal,
    Anomaly
}

/// <summary>Outcome of validating a configuration or a reading.</summary>
public class ValidationResult
{
    public List<string> Errors { get; } = new();
    public bool IsValid => Errors.Count == 0;

    public static ValidationResult Success() => new();
}
