using TemperatureMonitor.Models;
using static TemperatureMonitor.Tests.TestHelpers;

namespace TemperatureMonitor.Tests;

/// <summary>Controlled abnormal readings run through the full pipeline (validate, threshold, anomaly, store, log).</summary>
public class AbnormalReadingTests
{
    private static void FeedSteadyReadings(ManagerFixture f, string sensorId, int count = 5, double value = 23)
    {
        for (int i = 0; i < count; i++)
            f.Manager.InjectAbnormalReading(sensorId, value);
    }

    [Fact]
    public void WarningTemperature_IsStoredWithWarningStatusAndLogged()
    {
        using var f = new ManagerFixture();

        var reading = f.Manager.InjectAbnormalReading("S1", 27);

        Assert.Equal(ThresholdStatus.Warning, reading!.ThresholdStatus);
        Assert.Equal(ThresholdStatus.Warning, f.Repository.Retrieve("S1").Single().ThresholdStatus);
        Assert.Contains(f.Logger.Lines, l => l.Contains("ALERT WARNING S1"));
    }

    [Fact]
    public void CriticalTemperature_IsStoredWithCriticalStatusAndLogged()
    {
        using var f = new ManagerFixture();

        var reading = f.Manager.InjectAbnormalReading("S1", 35);

        Assert.Equal(ThresholdStatus.Critical, reading!.ThresholdStatus);
        Assert.Contains(f.Logger.Lines, l => l.Contains("ALERT CRITICAL S1"));
    }

    [Fact]
    public void SuddenJumpAfterFiveSteadyReadings_IsFlaggedAsAnomaly()
    {
        using var f = new ManagerFixture();
        FeedSteadyReadings(f, "S1");

        var reading = f.Manager.InjectAbnormalReading("S1", 26.5);

        Assert.Equal(AnomalyStatus.Anomaly, reading!.AnomalyStatus);
        Assert.Equal(ThresholdStatus.Warning, reading.ThresholdStatus);
        Assert.Contains(f.Logger.Lines, l => l.Contains("ANOMALY S1"));
        Assert.Equal(AnomalyStatus.Anomaly, f.Repository.Retrieve("S1").Last().AnomalyStatus);
    }

    [Fact]
    public void SmallChangeAfterFiveSteadyReadings_IsNormal()
    {
        using var f = new ManagerFixture();
        FeedSteadyReadings(f, "S1");

        var reading = f.Manager.InjectAbnormalReading("S1", 24.5);

        Assert.Equal(AnomalyStatus.Normal, reading!.AnomalyStatus);
    }

    [Fact]
    public void AbnormalReadingBeforeFiveReadings_IsNotEvaluated()
    {
        using var f = new ManagerFixture();
        FeedSteadyReadings(f, "S1", count: 4);

        var reading = f.Manager.InjectAbnormalReading("S1", 40);

        Assert.Equal(AnomalyStatus.NotEvaluated, reading!.AnomalyStatus);
    }

    [Fact]
    public void AnomalyDetection_UsesOnlySameSensorHistory()
    {
        using var f = new ManagerFixture();
        FeedSteadyReadings(f, "S2"); // S2 has history, S1 does not

        var reading = f.Manager.InjectAbnormalReading("S1", 40);

        Assert.Equal(AnomalyStatus.NotEvaluated, reading!.AnomalyStatus);
    }

    [Fact]
    public void TemperatureOutsideLimits_IsRejectedNotStoredAndLogged()
    {
        using var f = new ManagerFixture();

        var reading = f.Manager.InjectAbnormalReading("S1", 150);

        Assert.Null(reading);
        Assert.Empty(f.Repository.Retrieve("S1"));
        Assert.Empty(f.Manager.GetHistory("S1"));
        Assert.Contains(f.Logger.Lines, l => l.Contains("REJECTED S1") && l.Contains("outside the sensor limits"));
    }

    [Fact]
    public void RejectedReading_IsNotUsedForAnomalyOrSmoothing()
    {
        using var f = new ManagerFixture();
        FeedSteadyReadings(f, "S1");
        f.Manager.InjectAbnormalReading("S1", 999); // rejected

        Assert.Equal(23, f.Manager.SmoothData("S1"));
        Assert.Equal(5, f.Manager.GetHistory("S1").Count);
    }

    [Fact]
    public void InjectAbnormalReading_UnknownSensor_ReturnsNull()
    {
        using var f = new ManagerFixture();
        Assert.Null(f.Manager.InjectAbnormalReading("NOPE", 30));
    }

    [Fact]
    public void ProcessReading_UnregisteredSensor_IsRejectedAndLogged()
    {
        using var f = new ManagerFixture();

        var result = f.Manager.ProcessReading(Reading(23, Config("GHOST")));

        Assert.Null(result);
        Assert.Contains(f.Logger.Lines, l => l.Contains("REJECTED GHOST"));
    }

    [Fact]
    public void ProcessReading_MissingName_IsRejected()
    {
        using var f = new ManagerFixture();
        var reading = Reading(23, Config("S1"));
        reading.SensorName = null;

        Assert.Null(f.Manager.ProcessReading(reading));
        Assert.Empty(f.Repository.Retrieve("S1"));
    }

    [Fact]
    public void DetectAnomaly_OnManager_UsesSensorHistory()
    {
        using var f = new ManagerFixture();
        FeedSteadyReadings(f, "S1");

        Assert.Equal(AnomalyStatus.Anomaly, f.Manager.DetectAnomaly(Reading(30, Config("S1"))));
        Assert.Equal(AnomalyStatus.NotEvaluated, f.Manager.DetectAnomaly(Reading(30, Config("S2"))));
    }

    [Fact]
    public void SmoothData_OnManager_AveragesLatestFiveValidReadings()
    {
        using var f = new ManagerFixture();
        foreach (var t in new[] { 10.0, 22, 23, 24, 25, 26 })
            f.Manager.InjectAbnormalReading("S1", t);

        Assert.Equal(24, f.Manager.SmoothData("S1"));
    }
}
