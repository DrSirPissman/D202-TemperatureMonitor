using System.Collections.Concurrent;

namespace TemperatureMonitor.Services;

/// <summary>
/// Produces simulated temperatures around 22–24 °C with small random variation.
/// Controlled abnormal values can be queued for a sensor; the next reading for that sensor uses the queued value.
/// </summary>
public class TemperatureSimulator
{
    public const double BaseTemperature = 23.0;
    public const double MaxVariation = 1.0;

    private readonly Random _random;
    private readonly object _randomLock = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<double>> _abnormalQueues = new();

    public TemperatureSimulator(int? seed = null)
    {
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    /// <summary>Queues a controlled (e.g. abnormal) temperature to be used for the sensor's next reading.</summary>
    public void QueueAbnormalReading(string sensorId, double temperature)
    {
        _abnormalQueues.GetOrAdd(sensorId, _ => new ConcurrentQueue<double>()).Enqueue(temperature);
    }

    /// <summary>Returns the next temperature for a sensor: a queued controlled value if one exists, otherwise 22–24 °C.</summary>
    public double NextTemperature(string sensorId)
    {
        if (_abnormalQueues.TryGetValue(sensorId, out var queue) && queue.TryDequeue(out double queued))
            return queued;

        lock (_randomLock)
        {
            double variation = (_random.NextDouble() * 2 - 1) * MaxVariation; // -1.0 to +1.0
            return Math.Round(BaseTemperature + variation, 2);
        }
    }

    /// <summary>Removes any queued controlled values for a sensor.</summary>
    public void ClearQueue(string sensorId) => _abnormalQueues.TryRemove(sensorId, out _);
}
