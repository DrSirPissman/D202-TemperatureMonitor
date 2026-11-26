using TemperatureMonitor.Models;
using TemperatureMonitor.Services;

namespace TemperatureMonitor.Tests;

/// <summary>Repository that keeps readings in a list instead of SQLite, so manager tests are fast and isolated.</summary>
public sealed class InMemoryRepository : IReadingRepository
{
    private readonly object _lock = new();
    private readonly List<SensorReading> _readings = new();

    public bool IsDisposed { get; private set; }

    public void Store(SensorReading reading)
    {
        lock (_lock)
            _readings.Add(reading);
    }

    public IReadOnlyList<SensorReading> Retrieve(string? sensorId = null)
    {
        lock (_lock)
            return _readings.Where(r => sensorId == null || r.SensorId == sensorId).ToList();
    }

    public void Dispose() => IsDisposed = true;
}

/// <summary>A SensorManager wired to in-memory fakes, with two valid sensors (S1, S2) registered.</summary>
public sealed class ManagerFixture : IDisposable
{
    public InMemoryRepository Repository { get; } = new();
    public InMemoryLogger Logger { get; } = new();
    public TemperatureSimulator Simulator { get; } = new(seed: 42);
    public SensorManager Manager { get; }

    public ManagerFixture(bool registerDefaults = true, int intervalMs = 10)
    {
        Manager = new SensorManager(Repository, Logger, Simulator, TimeSpan.FromMilliseconds(intervalMs));
        if (registerDefaults)
        {
            Manager.InitialiseSensor(new[]
            {
                TestHelpers.Config("S1", "Rack A", "Row 1"),
                TestHelpers.Config("S2", "Rack B", "Row 2")
            });
        }
    }

    public void Dispose() => Manager.ExitApplication();
}
