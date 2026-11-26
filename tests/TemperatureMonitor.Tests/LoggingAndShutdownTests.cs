using TemperatureMonitor.Models;
using TemperatureMonitor.Services;
using static TemperatureMonitor.Tests.TestHelpers;

namespace TemperatureMonitor.Tests;

public class LoggingTests
{
    [Fact]
    public void FileLogger_WritesTimestampedLinesWithLevel()
    {
        string path = TempPath("app.log");
        using (var logger = new FileLogger(path))
        {
            logger.Log(LogLevel.Info, "hello");
            logger.Log(LogLevel.Error, "boom");
        }

        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        Assert.Contains("[INFO] hello", lines[0]);
        Assert.Contains("[ERROR] boom", lines[1]);
    }

    [Fact]
    public void FileLogger_AppendsToExistingFile()
    {
        string path = TempPath("app.log");
        using (var first = new FileLogger(path)) first.Log(LogLevel.Info, "one");
        using (var second = new FileLogger(path)) second.Log(LogLevel.Info, "two");

        Assert.Equal(2, File.ReadAllLines(path).Length);
    }

    [Fact]
    public void FileLogger_AfterDispose_IgnoresMessages()
    {
        string path = TempPath("app.log");
        var logger = new FileLogger(path);
        logger.Dispose();

        logger.Log(LogLevel.Info, "ignored"); // must not throw

        Assert.Empty(File.ReadAllLines(path));
        Assert.Equal(path, logger.FilePath);
    }

    [Fact]
    public void FileLogger_ConcurrentWrites_AllLinesWritten()
    {
        string path = TempPath("app.log");
        using (var logger = new FileLogger(path))
        {
            Parallel.For(0, 200, i => logger.Log(LogLevel.Info, $"line {i}"));
        }
        Assert.Equal(200, File.ReadAllLines(path).Length);
    }

    [Fact]
    public void LogData_AcceptedReading_LogsAcceptedAndAnomalyOutcome()
    {
        using var f = new ManagerFixture();
        var reading = Reading(23, Config("S1"));

        f.Manager.LogData(reading, accepted: true, Array.Empty<string>());

        Assert.Contains(f.Logger.Lines, l => l.Contains("ACCEPTED") && l.Contains("S1"));
        Assert.Contains(f.Logger.Lines, l => l.Contains("Anomaly check S1: NotEvaluated"));
    }

    [Fact]
    public void LogData_RejectedReading_LogsValidationErrors()
    {
        using var f = new ManagerFixture();

        f.Manager.LogData(Reading(99, Config("S1")), accepted: false, new[] { "Too hot." });

        Assert.Contains(f.Logger.Lines, l => l.StartsWith("[WARNING] REJECTED S1") && l.Contains("Too hot."));
    }

    [Fact]
    public void Initialise_LogsLoadedSensors()
    {
        using var f = new ManagerFixture();
        Assert.Contains(f.Logger.Lines, l => l.Contains("Sensor S1 initialised"));
    }

    [Fact]
    public void FullRun_WritesReadingsToLogFile()
    {
        string logPath = TempPath("run.log");
        var manager = new SensorManager(new InMemoryRepository(), new FileLogger(logPath), new TemperatureSimulator(1), TimeSpan.FromMilliseconds(10));
        manager.InitialiseSensor(new[] { Config("S1") });
        manager.InjectAbnormalReading("S1", 31);
        manager.ExitApplication();

        string log = File.ReadAllText(logPath);
        Assert.Contains("ACCEPTED", log);
        Assert.Contains("ALERT CRITICAL S1", log);
        Assert.Contains("Application exiting", log);
    }
}

public class ShutdownTests
{
    [Fact]
    public void ExitApplication_StopsAllSensorsAndClosesResources()
    {
        var f = new ManagerFixture();
        f.Manager.StartAllSensors();

        f.Manager.ExitApplication();

        Assert.True(f.Manager.IsShutDown);
        Assert.False(f.Manager.IsRunning("S1"));
        Assert.False(f.Manager.IsRunning("S2"));
        Assert.True(f.Repository.IsDisposed);
        Assert.True(f.Logger.IsDisposed);
        Assert.Contains(f.Logger.Lines, l => l.Contains("Application exiting. 2 running sensor(s) stopped."));
    }

    [Fact]
    public void ExitApplication_CalledTwice_DoesNotThrow()
    {
        var f = new ManagerFixture();
        f.Manager.ExitApplication();
        f.Manager.ExitApplication();
        f.Manager.Dispose();
        Assert.True(f.Manager.IsShutDown);
    }

    [Fact]
    public void ExitApplication_NoReadingsAddedAfterShutdown()
    {
        var f = new ManagerFixture();
        f.Manager.StartAllSensors();
        WaitUntil(() => f.Repository.Retrieve().Count > 0);

        f.Manager.ExitApplication();
        int count = f.Repository.Retrieve().Count;
        Thread.Sleep(100);

        Assert.Equal(count, f.Repository.Retrieve().Count);
    }

    [Fact]
    public void StartSensor_AfterShutdown_Throws()
    {
        var f = new ManagerFixture();
        f.Manager.ExitApplication();
        Assert.Throws<InvalidOperationException>(() => f.Manager.StartSensor("S1"));
    }

    [Fact]
    public void ExitApplication_WithSqlite_ReleasesDatabaseFile()
    {
        string path = TempPath("readings.db");
        var manager = new SensorManager(new SqliteReadingRepository(path), new InMemoryLogger(), new TemperatureSimulator(1), TimeSpan.FromMilliseconds(10));
        manager.InitialiseSensor(new[] { Config("S1") });
        manager.InjectAbnormalReading("S1", 23);

        manager.ExitApplication();

        File.Delete(path); // would fail on Windows if the file were still open
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void LoopErrors_AreLoggedAndDoNotCrash()
    {
        var logger = new InMemoryLogger();
        var manager = new SensorManager(new ThrowingRepository(), logger, new TemperatureSimulator(1), TimeSpan.FromMilliseconds(5));
        manager.InitialiseSensor(new[] { Config("S1") });

        manager.StartSensor("S1");
        Assert.True(WaitUntil(() => logger.Lines.Any(l => l.Contains("reading failed"))));
        Assert.True(manager.IsRunning("S1"));
        manager.ExitApplication();
    }

    private sealed class ThrowingRepository : IReadingRepository
    {
        public void Store(SensorReading reading) => throw new IOException("disk full");
        public IReadOnlyList<SensorReading> Retrieve(string? sensorId = null) => Array.Empty<SensorReading>();
        public void Dispose() { }
    }
}
