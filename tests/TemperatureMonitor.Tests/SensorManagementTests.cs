using TemperatureMonitor.Models;
using static TemperatureMonitor.Tests.TestHelpers;

namespace TemperatureMonitor.Tests;

public class SensorManagementTests
{
    [Fact]
    public void StartSensor_RegisteredSensor_StartsAndProducesReadings()
    {
        using var f = new ManagerFixture();

        Assert.True(f.Manager.StartSensor("S1"));
        Assert.True(f.Manager.IsRunning("S1"));
        Assert.True(WaitUntil(() => f.Repository.Retrieve("S1").Count >= 3));
    }

    [Fact]
    public void StartSensor_AlreadyRunning_ReturnsFalseAndDoesNotStartSecondLoop()
    {
        using var f = new ManagerFixture(intervalMs: 50);

        Assert.True(f.Manager.StartSensor("S1"));
        Assert.False(f.Manager.StartSensor("S1"));
        Assert.Contains(f.Logger.Lines, l => l.Contains("already running"));
    }

    [Fact]
    public void StartSensor_UnknownSensor_ReturnsFalse()
    {
        using var f = new ManagerFixture();
        Assert.False(f.Manager.StartSensor("NOPE"));
        Assert.False(f.Manager.StartSensor(""));
    }

    [Fact]
    public void StartAllSensors_StartsEveryRegisteredSensor()
    {
        using var f = new ManagerFixture();

        Assert.Equal(2, f.Manager.StartAllSensors());
        Assert.True(f.Manager.IsRunning("S1"));
        Assert.True(f.Manager.IsRunning("S2"));
        Assert.Equal(0, f.Manager.StartAllSensors()); // already running
    }

    [Fact]
    public void StopSensor_StopsOnlyThatSensor()
    {
        using var f = new ManagerFixture();
        f.Manager.StartAllSensors();

        Assert.True(f.Manager.StopSensor("S1"));

        Assert.False(f.Manager.IsRunning("S1"));
        Assert.True(f.Manager.IsRunning("S2"));
        int s2Before = f.Repository.Retrieve("S2").Count;
        Assert.True(WaitUntil(() => f.Repository.Retrieve("S2").Count > s2Before));
    }

    [Fact]
    public void StopSensor_KeepsDataAndAllowsRestart()
    {
        using var f = new ManagerFixture();
        f.Manager.StartSensor("S1");
        WaitUntil(() => f.Manager.GetHistory("S1").Count >= 2);
        f.Manager.StopSensor("S1");

        int stored = f.Repository.Retrieve("S1").Count;
        int history = f.Manager.GetHistory("S1").Count;
        Assert.True(stored >= 2);
        Assert.Equal(stored, history); // in-memory history kept after stop

        Assert.True(f.Manager.StartSensor("S1"));
        Assert.True(WaitUntil(() => f.Repository.Retrieve("S1").Count > stored));
    }

    [Fact]
    public void StopSensor_NotRunningOrUnknown_ReturnsFalse()
    {
        using var f = new ManagerFixture();
        Assert.False(f.Manager.StopSensor("S1"));
        Assert.False(f.Manager.StopSensor("NOPE"));
    }

    [Fact]
    public void StopAllSensors_StopsEverythingWithoutDeletingData()
    {
        using var f = new ManagerFixture();
        f.Manager.StartAllSensors();
        WaitUntil(() => f.Repository.Retrieve().Count >= 4);

        Assert.Equal(2, f.Manager.StopAllSensors());

        Assert.False(f.Manager.IsRunning("S1"));
        Assert.False(f.Manager.IsRunning("S2"));
        Assert.True(f.Repository.Retrieve().Count >= 4);
        Assert.Equal(2, f.Manager.GetSensors().Count);
    }

    [Fact]
    public void SimulateData_ProducesReadingWithIdentityLocationTemperatureAndTimestamp()
    {
        using var f = new ManagerFixture();

        var reading = f.Manager.SimulateData("S1");

        Assert.NotNull(reading);
        Assert.Equal("S1", reading!.SensorId);
        Assert.Equal("Rack A", reading.SensorName);
        Assert.Equal("Row 1", reading.Location);
        Assert.InRange(reading.Temperature, 22.0, 24.0);
        Assert.NotEqual(default, reading.Timestamp);
    }

    [Fact]
    public void SimulateData_UnknownSensor_ReturnsNull()
    {
        using var f = new ManagerFixture();
        Assert.Null(f.Manager.SimulateData("NOPE"));
    }

    [Fact]
    public void ResetSensor_StopsAndClearsHistoryButKeepsConfigAndStoredReadings()
    {
        using var f = new ManagerFixture();
        f.Manager.StartSensor("S1");
        WaitUntil(() => f.Manager.GetHistory("S1").Count >= 3);

        Assert.True(f.Manager.ResetSensor("S1"));

        Assert.False(f.Manager.IsRunning("S1"));
        Assert.Empty(f.Manager.GetHistory("S1"));
        Assert.Null(f.Manager.SmoothData("S1"));
        Assert.True(f.Manager.IsRegistered("S1"));
        Assert.True(f.Repository.Retrieve("S1").Count >= 3);
    }

    [Fact]
    public void ResetSensor_DoesNotAffectOtherSensors()
    {
        using var f = new ManagerFixture();
        for (int i = 0; i < 3; i++)
        {
            f.Manager.SimulateData("S1");
            f.Manager.SimulateData("S2");
        }

        f.Manager.ResetSensor("S1");

        Assert.Empty(f.Manager.GetHistory("S1"));
        Assert.Equal(3, f.Manager.GetHistory("S2").Count);
    }

    [Fact]
    public void ResetSensor_AnomalyHistoryStartsAgain()
    {
        using var f = new ManagerFixture();
        for (int i = 0; i < 5; i++)
            f.Manager.SimulateData("S1");

        f.Manager.ResetSensor("S1");
        var reading = f.Manager.InjectAbnormalReading("S1", 40);

        Assert.Equal(AnomalyStatus.NotEvaluated, reading!.AnomalyStatus);
    }

    [Fact]
    public void ResetAllSensors_ResetsEverySensor()
    {
        using var f = new ManagerFixture();
        f.Manager.SimulateData("S1");
        f.Manager.SimulateData("S2");

        Assert.Equal(2, f.Manager.ResetAllSensors());
        Assert.Empty(f.Manager.GetHistory("S1"));
        Assert.Empty(f.Manager.GetHistory("S2"));
    }

    [Fact]
    public void ResetSensor_Unknown_ReturnsFalse()
    {
        using var f = new ManagerFixture();
        Assert.False(f.Manager.ResetSensor("NOPE"));
    }

    [Fact]
    public void DeleteSensor_RemovesFromRegistrationButKeepsHistoricalReadings()
    {
        using var f = new ManagerFixture();
        f.Manager.StartSensor("S1");
        WaitUntil(() => f.Repository.Retrieve("S1").Count >= 2);

        Assert.True(f.Manager.DeleteSensor("S1"));

        Assert.False(f.Manager.IsRegistered("S1"));
        Assert.False(f.Manager.IsRunning("S1"));
        Assert.False(f.Manager.StartSensor("S1"));
        Assert.True(f.Manager.RetrieveData("S1").Count >= 2);
    }

    [Fact]
    public void DeleteSensor_DoesNotAffectOtherSensors()
    {
        using var f = new ManagerFixture();
        f.Manager.StartAllSensors();

        f.Manager.DeleteSensor("S1");

        Assert.True(f.Manager.IsRunning("S2"));
        Assert.True(f.Manager.IsRegistered("S2"));
    }

    [Fact]
    public void DeleteSensor_Unknown_ReturnsFalse()
    {
        using var f = new ManagerFixture();
        Assert.False(f.Manager.DeleteSensor("NOPE"));
    }

    [Fact]
    public void Sensors_KeepSeparateInMemoryHistories()
    {
        using var f = new ManagerFixture();
        f.Manager.InjectAbnormalReading("S1", 40);
        f.Manager.InjectAbnormalReading("S1", 41);
        f.Manager.InjectAbnormalReading("S2", 20);

        Assert.Equal(new[] { 40.0, 41.0 }, f.Manager.GetHistory("S1"));
        Assert.Equal(new[] { 20.0 }, f.Manager.GetHistory("S2"));
    }

    [Fact]
    public void History_IsCappedAtMaximum()
    {
        using var f = new ManagerFixture();
        for (int i = 0; i < SensorManagerConstants.Overflow; i++)
            f.Manager.SimulateData("S1");

        Assert.Equal(TemperatureMonitor.Services.SensorManager.MaxHistory, f.Manager.GetHistory("S1").Count);
    }

    [Fact]
    public void UpdateThresholds_ValidValues_AreApplied()
    {
        using var f = new ManagerFixture();

        var result = f.Manager.UpdateThresholds("S1", 24, 25);

        Assert.True(result.IsValid);
        Assert.Equal(ThresholdStatus.Critical, f.Manager.InjectAbnormalReading("S1", 25)!.ThresholdStatus);
        Assert.Equal(26, f.Manager.GetSensors().Single(s => s.Id == "S2").WarningThreshold); // S2 unchanged
    }

    [Fact]
    public void UpdateThresholds_InvalidValues_AreRejected()
    {
        using var f = new ManagerFixture();

        var result = f.Manager.UpdateThresholds("S1", 30, 26);

        Assert.False(result.IsValid);
        Assert.Equal(26, f.Manager.GetSensors().Single(s => s.Id == "S1").WarningThreshold);
    }

    [Fact]
    public void UpdateThresholds_UnknownSensor_IsRejected()
    {
        using var f = new ManagerFixture();
        Assert.False(f.Manager.UpdateThresholds("NOPE", 24, 25).IsValid);
    }

    [Fact]
    public void Constructor_NullDependencies_Throw()
    {
        var repo = new InMemoryRepository();
        var logger = new TemperatureMonitor.Services.InMemoryLogger();
        var sim = new TemperatureMonitor.Services.TemperatureSimulator();
        var interval = TimeSpan.FromSeconds(1);

        Assert.Throws<ArgumentNullException>(() => new TemperatureMonitor.Services.SensorManager(null!, logger, sim, interval));
        Assert.Throws<ArgumentNullException>(() => new TemperatureMonitor.Services.SensorManager(repo, null!, sim, interval));
        Assert.Throws<ArgumentNullException>(() => new TemperatureMonitor.Services.SensorManager(repo, logger, null!, interval));
    }
}

internal static class SensorManagerConstants
{
    public const int Overflow = TemperatureMonitor.Services.SensorManager.MaxHistory + 20;
}
