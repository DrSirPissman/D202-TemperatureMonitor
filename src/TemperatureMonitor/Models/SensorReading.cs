namespace TemperatureMonitor.Models;

/// <summary>
/// One temperature reading produced by a sensor, plus the statuses assigned while it is processed.
/// </summary>
public class SensorReading
{
    public string? SensorId { get; set; }
    public string? SensorName { get; set; }
    public string? Location { get; set; }
    public double Temperature { get; set; }
    public DateTime Timestamp { get; set; }
    public ThresholdStatus ThresholdStatus { get; set; } = ThresholdStatus.Normal;
    public AnomalyStatus AnomalyStatus { get; set; } = AnomalyStatus.NotEvaluated;

    public override string ToString() =>
        $"{Timestamp:yyyy-MM-dd HH:mm:ss} | {SensorId} ({SensorName}, {Location}) | {Temperature:F2} °C | {ThresholdStatus} | {AnomalyStatus}";
}
