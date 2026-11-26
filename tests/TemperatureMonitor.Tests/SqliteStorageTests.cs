using TemperatureMonitor.Models;
using TemperatureMonitor.Services;
using static TemperatureMonitor.Tests.TestHelpers;

namespace TemperatureMonitor.Tests;

public class SqliteStorageTests
{
    private static SensorReading Sample(string id, double temp, ThresholdStatus threshold = ThresholdStatus.Normal,
        AnomalyStatus anomaly = AnomalyStatus.NotEvaluated) => new()
    {
        SensorId = id,
        SensorName = "Name " + id,
        Location = "Loc " + id,
        Temperature = temp,
        Timestamp = new DateTime(2026, 11, 26, 10, 30, 15, DateTimeKind.Local),
        ThresholdStatus = threshold,
        AnomalyStatus = anomaly
    };

    [Fact]
    public void Store_ThenRetrieve_ReturnsAllFields()
    {
        string path = TempPath("readings.db");
        using var repo = new SqliteReadingRepository(path);

        repo.Store(Sample("S1", 27.25, ThresholdStatus.Warning, AnomalyStatus.Anomaly));

        var stored = repo.Retrieve("S1").Single();
        Assert.Equal("S1", stored.SensorId);
        Assert.Equal("Name S1", stored.SensorName);
        Assert.Equal("Loc S1", stored.Location);
        Assert.Equal(27.25, stored.Temperature);
        Assert.Equal(new DateTime(2026, 11, 26, 10, 30, 15), stored.Timestamp);
        Assert.Equal(ThresholdStatus.Warning, stored.ThresholdStatus);
        Assert.Equal(AnomalyStatus.Anomaly, stored.AnomalyStatus);
    }

    [Fact]
    public void Retrieve_FiltersBySensorOrReturnsAll()
    {
        using var repo = new SqliteReadingRepository(TempPath("readings.db"));
        repo.Store(Sample("S1", 23));
        repo.Store(Sample("S2", 24));
        repo.Store(Sample("S1", 25));

        Assert.Equal(2, repo.Retrieve("S1").Count);
        Assert.Single(repo.Retrieve("S2"));
        Assert.Equal(3, repo.Retrieve().Count);
        Assert.Empty(repo.Retrieve("NONE"));
    }

    [Fact]
    public void Retrieve_ReturnsOldestFirst()
    {
        using var repo = new SqliteReadingRepository(TempPath("readings.db"));
        repo.Store(Sample("S1", 21));
        repo.Store(Sample("S1", 22));

        Assert.Equal(new[] { 21.0, 22.0 }, repo.Retrieve("S1").Select(r => r.Temperature));
    }

    [Fact]
    public void Readings_PersistAcrossApplicationRestart()
    {
        string path = TempPath("readings.db");
        using (var first = new SqliteReadingRepository(path))
        {
            first.Store(Sample("S1", 23));
            first.Store(Sample("S1", 31, ThresholdStatus.Critical));
        }

        using var second = new SqliteReadingRepository(path);
        var readings = second.Retrieve("S1");

        Assert.Equal(2, readings.Count);
        Assert.Equal(ThresholdStatus.Critical, readings[1].ThresholdStatus);
    }

    [Fact]
    public void Readings_PersistAfterResetDeleteAndRestart()
    {
        string path = TempPath("readings.db");
        var first = new SensorManager(new SqliteReadingRepository(path), new InMemoryLogger(), new TemperatureSimulator(1), TimeSpan.FromMilliseconds(10));
        first.InitialiseSensor(new[] { Config("S1"), Config("S2") });
        first.InjectAbnormalReading("S1", 23);
        first.InjectAbnormalReading("S2", 24);
        first.ResetSensor("S1");
        first.DeleteSensor("S2");
        first.ExitApplication();

        var second = new SensorManager(new SqliteReadingRepository(path), new InMemoryLogger(), new TemperatureSimulator(1), TimeSpan.FromMilliseconds(10));
        second.InitialiseSensor(new[] { Config("S1") }); // S2 no longer configured

        Assert.Single(second.RetrieveData("S1"));
        Assert.Single(second.RetrieveData("S2")); // deleted sensor's history still there
        Assert.Equal(2, second.RetrieveData().Count);
        second.ExitApplication();
    }

    [Fact]
    public void Store_AfterDispose_Throws()
    {
        var repo = new SqliteReadingRepository(TempPath("readings.db"));
        repo.Dispose();

        Assert.Throws<ObjectDisposedException>(() => repo.Store(Sample("S1", 23)));
        Assert.Throws<ObjectDisposedException>(() => repo.Retrieve());
    }

    [Fact]
    public void Store_Null_Throws()
    {
        using var repo = new SqliteReadingRepository(TempPath("readings.db"));
        Assert.Throws<ArgumentNullException>(() => repo.Store(null!));
    }

    [Fact]
    public void Constructor_CreatesDatabaseFile()
    {
        string path = TempPath("readings.db");
        using var repo = new SqliteReadingRepository(path);
        Assert.True(File.Exists(path));
        Assert.Equal(path, repo.DatabasePath);
    }

    [Fact]
    public void RunningSensors_StoreReadingsConcurrently()
    {
        string path = TempPath("readings.db");
        var manager = new SensorManager(new SqliteReadingRepository(path), new InMemoryLogger(), new TemperatureSimulator(1), TimeSpan.FromMilliseconds(5));
        manager.InitialiseSensor(new[] { Config("S1"), Config("S2"), Config("S3") });

        manager.StartAllSensors();
        Assert.True(WaitUntil(() => manager.RetrieveData().Select(r => r.SensorId).Distinct().Count() == 3 &&
                                    manager.RetrieveData().Count >= 9));
        manager.ExitApplication();
    }
}
