using TemperatureMonitor.Services;
using TemperatureMonitor.UI;
using static TemperatureMonitor.Tests.TestHelpers;

namespace TemperatureMonitor.Tests;

public class ConsoleMenuTests
{
    /// <summary>Runs the menu with scripted input lines and returns everything it printed.</summary>
    private static string RunMenuWithConfig(ManagerFixture f, string configPath, params string[] inputLines)
    {
        var input = new StringReader(string.Join(Environment.NewLine, inputLines) + Environment.NewLine);
        var output = new StringWriter();
        new ConsoleMenu(f.Manager, input, output, configPath).Run();
        return output.ToString();
    }

    private static string RunMenu(ManagerFixture f, params string[] inputLines) =>
        RunMenuWithConfig(f, TempPath("sensors.json"), inputLines);

    [Fact]
    public void Exit_StopsSensorsAndShutsDown()
    {
        var f = new ManagerFixture();
        f.Manager.StartAllSensors();

        string output = RunMenu(f, "0");

        Assert.Contains("Goodbye", output);
        Assert.True(f.Manager.IsShutDown);
        Assert.False(f.Manager.IsRunning("S1"));
    }

    [Fact]
    public void EndOfInput_ShutsDownCleanly()
    {
        var f = new ManagerFixture();
        string output = RunMenu(f); // no input at all
        Assert.True(f.Manager.IsShutDown);
        Assert.Contains("Goodbye", output);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("99")]
    [InlineData("")]
    [InlineData("-1")]
    public void InvalidMenuInput_ShowsMessageAndDoesNotCrash(string bad)
    {
        var f = new ManagerFixture();
        string output = RunMenu(f, bad, "0");
        Assert.Contains("Invalid option", output);
        Assert.True(f.Manager.IsShutDown);
    }

    [Theory]
    [InlineData("x")]
    [InlineData("0")]
    [InlineData("5")]
    public void InvalidSensorNumber_ShowsMessage(string bad)
    {
        var f = new ManagerFixture();
        string output = RunMenu(f, "1", bad, "0");
        Assert.Contains("Invalid sensor number", output);
        Assert.False(f.Manager.IsRunning("S1"));
    }

    [Fact]
    public void StartAndStopOneSensor()
    {
        var f = new ManagerFixture();
        var menu = new ConsoleMenu(f.Manager, new StringReader("1\n1\n"), new StringWriter(), TempPath("s.json"));

        menu.HandleChoice("1"); // start sensor 1 (S1)
        Assert.True(f.Manager.IsRunning("S1"));
        Assert.False(f.Manager.IsRunning("S2"));

        menu.HandleChoice("3"); // stop sensor 1
        Assert.False(f.Manager.IsRunning("S1"));
        f.Dispose();
    }

    [Fact]
    public void StartAlreadyRunningSensor_SaysSo()
    {
        var f = new ManagerFixture();
        string output = RunMenu(f, "1", "1", "1", "1", "0");
        Assert.Contains("already running", output);
    }

    [Fact]
    public void StopSensorThatIsNotRunning_SaysSo()
    {
        var f = new ManagerFixture();
        string output = RunMenu(f, "3", "1", "0");
        Assert.Contains("is not running", output);
    }

    [Fact]
    public void StartAllAndStopAll()
    {
        var f = new ManagerFixture();
        string output = RunMenu(f, "2", "4", "0");
        Assert.Contains("2 sensor(s) started.", output);
        Assert.Contains("2 sensor(s) stopped.", output);
    }

    [Fact]
    public void ViewReadings_ForOneSensor_ShowsStoredReadings()
    {
        var f = new ManagerFixture();
        f.Manager.InjectAbnormalReading("S1", 27);

        string output = RunMenu(f, "5", "S1", "0");

        Assert.Contains("1 stored reading(s) for sensor S1", output);
        Assert.Contains("27.00", output);
        Assert.Contains("Warning", output);
    }

    [Fact]
    public void ViewReadings_ForDeletedSensor_StillShowsHistory()
    {
        var f = new ManagerFixture();
        f.Manager.InjectAbnormalReading("S2", 23);
        f.Manager.DeleteSensor("S2");

        string output = RunMenu(f, "5", "S2", "0");

        Assert.Contains("1 stored reading(s) for sensor S2", output);
    }

    [Fact]
    public void ViewReadings_NoIdOrNoReadings_ShowsMessage()
    {
        var f = new ManagerFixture();
        string output = RunMenu(f, "5", "", "5", "S9", "6", "0");
        Assert.Contains("No sensor ID entered", output);
        Assert.Contains("No stored readings for sensor S9", output);
        Assert.Contains("No stored readings for all sensors", output);
    }

    [Fact]
    public void ViewReadings_AllSensors_ShowsLatestTwenty()
    {
        var f = new ManagerFixture();
        for (int i = 0; i < 25; i++)
            f.Manager.SimulateData(i % 2 == 0 ? "S1" : "S2");

        string output = RunMenu(f, "6", "0");

        Assert.Contains("25 stored reading(s) for all sensors. Latest 20:", output);
    }

    [Fact]
    public void Analyse_OneSensor_ShowsSmoothedAndSummary()
    {
        var f = new ManagerFixture();
        foreach (var t in new[] { 23.0, 23, 23, 23, 23, 31 })
            f.Manager.InjectAbnormalReading("S1", t);

        string output = RunMenu(f, "7", "1", "0");

        Assert.Contains("Analysis for sensor S1", output);
        Assert.Contains("Smoothed temperature (last 5 valid readings): 24.60", output);
        Assert.Contains("Stored readings: 6", output);
        Assert.Contains("Critical: 1", output);
        Assert.Contains("Anomalies: 1", output);
    }

    [Fact]
    public void Analyse_SensorWithNoReadings_ShowsMessage()
    {
        var f = new ManagerFixture();
        string output = RunMenu(f, "7", "2", "0");
        Assert.Contains("no readings since the sensor was started or reset", output);
        Assert.Contains("No stored readings.", output);
    }

    [Fact]
    public void AnalyseAll_ShowsEverySensor()
    {
        var f = new ManagerFixture();
        string output = RunMenu(f, "8", "0");
        Assert.Contains("Analysis for sensor S1", output);
        Assert.Contains("Analysis for sensor S2", output);
    }

    [Fact]
    public void ConfigureThresholds_ValidValues_AreAppliedAndSaved()
    {
        var f = new ManagerFixture();
        string configPath = TempPath("sensors.json");

        string output = RunMenuWithConfig(f, configPath, "9", "1", "24", "28", "0");

        Assert.Contains("Thresholds updated and saved.", output);
        var saved = ConfigurationStore.Load(configPath);
        Assert.Equal(24, saved.Single(s => s.Id == "S1").WarningThreshold);
        Assert.Equal(28, saved.Single(s => s.Id == "S1").CriticalThreshold);
    }

    [Fact]
    public void ConfigureThresholds_SavedSettingsReloadOnNextStart()
    {
        var f = new ManagerFixture();
        string configPath = TempPath("sensors.json");
        RunMenuWithConfig(f, configPath, "9", "1", "24", "28", "0");

        using var restarted = new ManagerFixture(registerDefaults: false);
        restarted.Manager.InitialiseSensorsFromFile(configPath);

        Assert.Equal(24, restarted.Manager.GetSensors().Single(s => s.Id == "S1").WarningThreshold);
    }

    [Fact]
    public void ConfigureThresholds_InvalidValues_AreRejected()
    {
        var f = new ManagerFixture();
        string output = RunMenu(f, "9", "1", "30", "25", "0");
        Assert.Contains("Thresholds not changed", output);
    }

    [Fact]
    public void ConfigureThresholds_NonNumericInput_IsRejected()
    {
        var f = new ManagerFixture();
        string output = RunMenu(f, "9", "1", "hot", "9", "1", "25", "warm", "0");
        Assert.Equal(2, CountOccurrences(output, "Invalid number."));
        Assert.Equal(26, f.Manager.GetSensors().Single(s => s.Id == "S1").WarningThreshold);
    }

    [Fact]
    public void ResetSensor_ClearsHistoryKeepsStoredReadings()
    {
        var f = new ManagerFixture();
        f.Manager.InjectAbnormalReading("S1", 23);

        string output = RunMenu(f, "10", "1", "0");

        Assert.Contains("Sensor S1 reset. Stored readings were kept.", output);
        Assert.Empty(f.Manager.GetHistory("S1"));
        Assert.Single(f.Repository.Retrieve("S1"));
    }

    [Fact]
    public void ResetAllSensors_ResetsBoth()
    {
        var f = new ManagerFixture();
        string output = RunMenu(f, "11", "0");
        Assert.Contains("2 sensor(s) reset.", output);
    }

    [Fact]
    public void DeleteSensor_Confirmed_RemovesSensorAndSavesConfig()
    {
        var f = new ManagerFixture();
        string configPath = TempPath("sensors.json");

        string output = RunMenuWithConfig(f, configPath, "12", "1", "y", "0");

        Assert.Contains("Sensor S1 deleted.", output);
        Assert.False(f.Manager.IsRegistered("S1"));
        Assert.DoesNotContain(ConfigurationStore.Load(configPath), s => s.Id == "S1");
    }

    [Fact]
    public void DeleteSensor_NotConfirmed_IsCancelled()
    {
        var f = new ManagerFixture();
        string output = RunMenu(f, "12", "1", "n", "0");
        Assert.Contains("Delete cancelled.", output);
        Assert.True(f.Manager.IsRegistered("S1"));
    }

    [Fact]
    public void ControlledReading_Accepted_ShowsStatuses()
    {
        var f = new ManagerFixture();
        string output = RunMenu(f, "13", "1", "35", "0");
        Assert.Contains("Reading accepted", output);
        Assert.Contains("Critical", output);
    }

    [Fact]
    public void ControlledReading_OutOfRange_IsRejected()
    {
        var f = new ManagerFixture();
        string output = RunMenu(f, "13", "1", "500", "13", "1", "abc", "0");
        Assert.Contains("Reading rejected by validation", output);
        Assert.Contains("Invalid number.", output);
    }

    [Fact]
    public void ShowStatus_ListsSensorsAndRunningState()
    {
        var f = new ManagerFixture();
        f.Manager.StartSensor("S1");

        string output = RunMenu(f, "14", "0");

        Assert.Contains("S1 | Rack A | Row 1 | Running", output);
        Assert.Contains("S2 | Rack B | Row 2 | Stopped", output);
    }

    [Fact]
    public void NoSensorsRegistered_MenuOptionsReportIt()
    {
        var f = new ManagerFixture(registerDefaults: false);
        string output = RunMenu(f, "1", "8", "14", "0");
        Assert.Equal(3, CountOccurrences(output, "No sensors registered."));
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0, index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}
