using TemperatureMonitor.Models;
using TemperatureMonitor.Services;
using static TemperatureMonitor.Tests.TestHelpers;

namespace TemperatureMonitor.Tests;

public class ReadingValidationTests
{
    private readonly SensorConfig _config = Config(min: -10, max: 60);

    [Theory]
    [InlineData(-10.0)] // exactly the minimum
    [InlineData(60.0)]  // exactly the maximum
    [InlineData(23.5)]  // normal
    public void ValidateData_TemperatureWithinLimits_IsValid(double temperature)
    {
        Assert.True(TemperatureProcessor.ValidateData(Reading(temperature, _config), _config).IsValid);
    }

    [Theory]
    [InlineData(-10.01)] // just below the minimum
    [InlineData(60.01)]  // just above the maximum
    [InlineData(-273.0)]
    [InlineData(500.0)]
    public void ValidateData_TemperatureOutsideLimits_IsInvalid(double temperature)
    {
        var result = TemperatureProcessor.ValidateData(Reading(temperature, _config), _config);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("outside the sensor limits"));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ValidateData_NotARealNumber_IsInvalid(double temperature)
    {
        Assert.False(TemperatureProcessor.ValidateData(Reading(temperature, _config), _config).IsValid);
    }

    [Fact]
    public void ValidateData_NullReading_IsInvalid()
    {
        Assert.False(TemperatureProcessor.ValidateData(null, _config).IsValid);
    }

    [Fact]
    public void ValidateData_MissingFields_ReportsEachError()
    {
        var reading = new SensorReading { Temperature = 23 };

        var result = TemperatureProcessor.ValidateData(reading, _config);

        Assert.Equal(4, result.Errors.Count); // id, name, location, timestamp
    }
}

public class ThresholdTests
{
    private readonly SensorConfig _config = Config(warning: 26, critical: 30);

    [Theory]
    [InlineData(22.0, ThresholdStatus.Normal)]
    [InlineData(25.99, ThresholdStatus.Normal)]   // just below warning
    [InlineData(26.0, ThresholdStatus.Warning)]   // exactly warning
    [InlineData(29.99, ThresholdStatus.Warning)]  // just below critical
    [InlineData(30.0, ThresholdStatus.Critical)]  // exactly critical
    [InlineData(45.0, ThresholdStatus.Critical)]
    public void CheckThreshold_AssignsCorrectStatus(double temperature, ThresholdStatus expected)
    {
        Assert.Equal(expected, TemperatureProcessor.CheckThreshold(temperature, _config));
    }

    [Fact]
    public void CheckThreshold_UsesEachSensorsOwnThresholds()
    {
        var strict = Config(warning: 24, critical: 25);
        Assert.Equal(ThresholdStatus.Critical, TemperatureProcessor.CheckThreshold(25, strict));
        Assert.Equal(ThresholdStatus.Normal, TemperatureProcessor.CheckThreshold(25, _config));
    }
}

public class AnomalyDetectionTests
{
    private static readonly double[] Steady = { 23, 23, 23, 23, 23 };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void DetectAnomaly_FewerThanFivePrevious_ReturnsNotEvaluated(int count)
    {
        var history = Enumerable.Repeat(23.0, count).ToList();
        Assert.Equal(AnomalyStatus.NotEvaluated, TemperatureProcessor.DetectAnomaly(40, history));
    }

    [Theory]
    [InlineData(26.0)]  // exactly +3
    [InlineData(20.0)]  // exactly -3
    [InlineData(35.0)]
    public void DetectAnomaly_DifferenceOfAtLeastThree_ReturnsAnomaly(double latest)
    {
        Assert.Equal(AnomalyStatus.Anomaly, TemperatureProcessor.DetectAnomaly(latest, Steady));
    }

    [Theory]
    [InlineData(25.99)]
    [InlineData(20.01)]
    [InlineData(23.0)]
    public void DetectAnomaly_DifferenceBelowThree_ReturnsNormal(double latest)
    {
        Assert.Equal(AnomalyStatus.Normal, TemperatureProcessor.DetectAnomaly(latest, Steady));
    }

    [Fact]
    public void DetectAnomaly_UsesOnlyLatestFivePrevious()
    {
        // Old readings of 10 °C should be ignored; the latest five average 23.
        var history = new List<double> { 10, 10, 10, 23, 23, 23, 23, 23 };
        Assert.Equal(AnomalyStatus.Normal, TemperatureProcessor.DetectAnomaly(24, history));
    }

    [Fact]
    public void DetectAnomaly_DecimalNoiseAtThreeDegrees_StillAnomaly()
    {
        var history = new List<double> { 22.1, 22.2, 22.3, 22.4, 22.5 }; // average 22.3
        Assert.Equal(AnomalyStatus.Anomaly, TemperatureProcessor.DetectAnomaly(25.3, history));
    }
}

public class SmoothingTests
{
    [Fact]
    public void SmoothData_NoReadings_ReturnsNull()
    {
        Assert.Null(TemperatureProcessor.SmoothData(new List<double>()));
    }

    [Fact]
    public void SmoothData_OneReading_ReturnsThatReading()
    {
        Assert.Equal(23.4, TemperatureProcessor.SmoothData(new List<double> { 23.4 }));
    }

    [Fact]
    public void SmoothData_FewerThanFive_AveragesAll()
    {
        Assert.Equal(23.0, TemperatureProcessor.SmoothData(new List<double> { 22, 23, 24 }));
    }

    [Fact]
    public void SmoothData_ExactlyFive_AveragesAll()
    {
        Assert.Equal(24.0, TemperatureProcessor.SmoothData(new List<double> { 22, 23, 24, 25, 26 }));
    }

    [Fact]
    public void SmoothData_MoreThanFive_AveragesLatestFive()
    {
        var readings = new List<double> { 100, 100, 22, 23, 24, 25, 26 };
        Assert.Equal(24.0, TemperatureProcessor.SmoothData(readings));
    }
}

public class SimulatorTests
{
    [Fact]
    public void NextTemperature_NormalReadings_StayBetween22And24()
    {
        var simulator = new TemperatureSimulator(seed: 1);
        for (int i = 0; i < 1000; i++)
        {
            double t = simulator.NextTemperature("S1");
            Assert.InRange(t, 22.0, 24.0);
        }
    }

    [Fact]
    public void NextTemperature_QueuedAbnormalValue_IsUsedOnceForThatSensorOnly()
    {
        var simulator = new TemperatureSimulator(seed: 1);
        simulator.QueueAbnormalReading("S1", 45);

        Assert.InRange(simulator.NextTemperature("S2"), 22.0, 24.0);
        Assert.Equal(45, simulator.NextTemperature("S1"));
        Assert.InRange(simulator.NextTemperature("S1"), 22.0, 24.0);
    }

    [Fact]
    public void ClearQueue_RemovesQueuedValues()
    {
        var simulator = new TemperatureSimulator(seed: 1);
        simulator.QueueAbnormalReading("S1", 45);
        simulator.ClearQueue("S1");
        Assert.InRange(simulator.NextTemperature("S1"), 22.0, 24.0);
    }
}
