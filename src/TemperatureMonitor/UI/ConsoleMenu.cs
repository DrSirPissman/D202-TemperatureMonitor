using System.Globalization;
using TemperatureMonitor.Models;
using TemperatureMonitor.Services;

namespace TemperatureMonitor.UI;

/// <summary>
/// Numeric console menu. Input and output are passed in as TextReader/TextWriter so the menu can be tested.
/// Invalid input never crashes the menu; the user is told and asked again.
/// </summary>
public class ConsoleMenu
{
    /// <summary>How many of the most recent stored readings are shown when viewing.</summary>
    public const int ReadingsToShow = 20;

    private readonly SensorManager _manager;
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly string _configPath;

    public ConsoleMenu(SensorManager manager, TextReader input, TextWriter output, string configPath)
    {
        _manager = manager;
        _input = input;
        _output = output;
        _configPath = configPath;
    }

    /// <summary>Shows the menu until the user chooses Exit (or input ends), then shuts down cleanly.</summary>
    public void Run()
    {
        bool running = true;
        while (running)
        {
            ShowMenu();
            string? line = _input.ReadLine();
            if (line == null)
                break; // input closed: treat as exit

            running = HandleChoice(line.Trim());
        }

        _output.WriteLine("Stopping all sensors and closing resources...");
        _manager.ExitApplication();
        _output.WriteLine("Goodbye.");
    }

    private void ShowMenu()
    {
        _output.WriteLine();
        _output.WriteLine("=== Data Centre Temperature Monitor ===");
        _output.WriteLine(" 1. Start a sensor");
        _output.WriteLine(" 2. Start all sensors");
        _output.WriteLine(" 3. Stop a sensor");
        _output.WriteLine(" 4. Stop all sensors");
        _output.WriteLine(" 5. View readings for a sensor");
        _output.WriteLine(" 6. View readings for all sensors");
        _output.WriteLine(" 7. Analyse temperatures for a sensor");
        _output.WriteLine(" 8. Analyse temperatures for all sensors");
        _output.WriteLine(" 9. Configure alert thresholds");
        _output.WriteLine("10. Reset a sensor");
        _output.WriteLine("11. Reset all sensors");
        _output.WriteLine("12. Delete a sensor");
        _output.WriteLine("13. Generate a controlled (abnormal) reading");
        _output.WriteLine("14. Show sensor status");
        _output.WriteLine(" 0. Exit");
        _output.Write("Choose an option: ");
    }

    /// <summary>Runs one menu choice. Returns false when the user chose Exit.</summary>
    public bool HandleChoice(string choice)
    {
        switch (choice)
        {
            case "1": WithSensor(id => Report(_manager.StartSensor(id), $"Sensor {id} started.", $"Sensor {id} is already running.")); break;
            case "2": _output.WriteLine($"{_manager.StartAllSensors()} sensor(s) started."); break;
            case "3": WithSensor(id => Report(_manager.StopSensor(id), $"Sensor {id} stopped.", $"Sensor {id} is not running.")); break;
            case "4": _output.WriteLine($"{_manager.StopAllSensors()} sensor(s) stopped."); break;
            case "5": ViewReadingsForOne(); break;
            case "6": ShowReadings(_manager.RetrieveData(), "all sensors"); break;
            case "7": WithSensor(Analyse); break;
            case "8": AnalyseAll(); break;
            case "9": WithSensor(ConfigureThresholds); break;
            case "10": WithSensor(id => Report(_manager.ResetSensor(id), $"Sensor {id} reset. Stored readings were kept.", $"Sensor {id} could not be reset.")); break;
            case "11": _output.WriteLine($"{_manager.ResetAllSensors()} sensor(s) reset. Stored readings were kept."); break;
            case "12": WithSensor(DeleteWithConfirmation); break;
            case "13": WithSensor(GenerateControlledReading); break;
            case "14": ShowStatus(); break;
            case "0": return false;
            default:
                _output.WriteLine("Invalid option. Please enter a number from the menu.");
                break;
        }
        return true;
    }

    // ------------------------------------------------------------------ actions

    private void ViewReadingsForOne()
    {
        // Deleted sensors are no longer registered, so readings can be viewed by typing any sensor ID.
        _output.Write("Enter sensor ID (deleted sensors included): ");
        string? id = _input.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(id))
        {
            _output.WriteLine("No sensor ID entered.");
            return;
        }
        ShowReadings(_manager.RetrieveData(id), $"sensor {id}");
    }

    private void ShowReadings(IReadOnlyList<SensorReading> readings, string label)
    {
        if (readings.Count == 0)
        {
            _output.WriteLine($"No stored readings for {label}.");
            return;
        }

        _output.WriteLine($"{readings.Count} stored reading(s) for {label}. Latest {Math.Min(ReadingsToShow, readings.Count)}:");
        foreach (var reading in readings.Skip(Math.Max(0, readings.Count - ReadingsToShow)))
            _output.WriteLine("  " + reading);
    }

    private void Analyse(string sensorId)
    {
        var stored = _manager.RetrieveData(sensorId);
        double? smoothed = _manager.SmoothData(sensorId);

        _output.WriteLine($"--- Analysis for sensor {sensorId} ---");
        _output.WriteLine(smoothed.HasValue
            ? $"Smoothed temperature (last {Math.Min(TemperatureProcessor.WindowSize, _manager.GetHistory(sensorId).Count)} valid readings): {smoothed.Value:F2} °C"
            : "Smoothed temperature: no readings since the sensor was started or reset.");

        if (stored.Count == 0)
        {
            _output.WriteLine("No stored readings.");
            return;
        }

        _output.WriteLine($"Stored readings: {stored.Count}");
        _output.WriteLine($"Latest: {stored[^1].Temperature:F2} °C ({stored[^1].ThresholdStatus}, {stored[^1].AnomalyStatus})");
        _output.WriteLine($"Min / Avg / Max: {stored.Min(r => r.Temperature):F2} / {stored.Average(r => r.Temperature):F2} / {stored.Max(r => r.Temperature):F2} °C");
        _output.WriteLine($"Warnings: {stored.Count(r => r.ThresholdStatus == ThresholdStatus.Warning)}, " +
                          $"Critical: {stored.Count(r => r.ThresholdStatus == ThresholdStatus.Critical)}, " +
                          $"Anomalies: {stored.Count(r => r.AnomalyStatus == AnomalyStatus.Anomaly)}");
    }

    private void AnalyseAll()
    {
        var sensors = _manager.GetSensors();
        if (sensors.Count == 0)
        {
            _output.WriteLine("No sensors registered.");
            return;
        }
        foreach (var sensor in sensors)
            Analyse(sensor.Id!);
    }

    private void ConfigureThresholds(string sensorId)
    {
        var config = _manager.GetSensors().First(c => string.Equals(c.Id, sensorId, StringComparison.OrdinalIgnoreCase));
        _output.WriteLine($"Current thresholds: warning {config.WarningThreshold} °C, critical {config.CriticalThreshold} °C " +
                          $"(limits {config.MinTemp} to {config.MaxTemp} °C).");

        double? warning = ReadNumber("New warning threshold (°C): ");
        if (warning == null) return;
        double? critical = ReadNumber("New critical threshold (°C): ");
        if (critical == null) return;

        var result = _manager.UpdateThresholds(sensorId, warning.Value, critical.Value);
        if (!result.IsValid)
        {
            _output.WriteLine("Thresholds not changed: " + string.Join(" ", result.Errors));
            return;
        }

        try
        {
            _manager.SaveConfiguration(_configPath);
            _output.WriteLine("Thresholds updated and saved.");
        }
        catch (IOException ex)
        {
            _output.WriteLine($"Thresholds updated but could not be saved: {ex.Message}");
        }
    }

    private void DeleteWithConfirmation(string sensorId)
    {
        _output.Write($"Delete sensor {sensorId}? Stored readings will be kept. Type Y to confirm: ");
        string? answer = _input.ReadLine()?.Trim();
        if (!string.Equals(answer, "Y", StringComparison.OrdinalIgnoreCase))
        {
            _output.WriteLine("Delete cancelled.");
            return;
        }

        Report(_manager.DeleteSensor(sensorId), $"Sensor {sensorId} deleted.", $"Sensor {sensorId} could not be deleted.");
        try
        {
            _manager.SaveConfiguration(_configPath);
        }
        catch (IOException ex)
        {
            _output.WriteLine($"Configuration could not be saved: {ex.Message}");
        }
    }

    private void GenerateControlledReading(string sensorId)
    {
        double? temperature = ReadNumber("Temperature to generate (°C): ");
        if (temperature == null) return;

        var reading = _manager.InjectAbnormalReading(sensorId, temperature.Value);
        _output.WriteLine(reading == null
            ? "Reading rejected by validation (see log)."
            : $"Reading accepted: {reading}");
    }

    private void ShowStatus()
    {
        var sensors = _manager.GetSensors();
        if (sensors.Count == 0)
        {
            _output.WriteLine("No sensors registered.");
            return;
        }

        foreach (var s in sensors)
        {
            string state = _manager.IsRunning(s.Id!) ? "Running" : "Stopped";
            _output.WriteLine($"  {s.Id} | {s.Name} | {s.Location} | {state} | warning {s.WarningThreshold} °C, critical {s.CriticalThreshold} °C");
        }
    }

    // ------------------------------------------------------------------ input helpers

    /// <summary>Lists registered sensors, asks for one by number, then runs the action. Invalid input is reported.</summary>
    private void WithSensor(Action<string> action)
    {
        string? sensorId = ChooseSensor();
        if (sensorId != null)
            action(sensorId);
    }

    private string? ChooseSensor()
    {
        var sensors = _manager.GetSensors();
        if (sensors.Count == 0)
        {
            _output.WriteLine("No sensors registered.");
            return null;
        }

        for (int i = 0; i < sensors.Count; i++)
            _output.WriteLine($"  {i + 1}. {sensors[i].Id} - {sensors[i].Name} ({sensors[i].Location})");
        _output.Write("Choose a sensor number: ");

        string? line = _input.ReadLine();
        if (int.TryParse(line?.Trim(), out int number) && number >= 1 && number <= sensors.Count)
            return sensors[number - 1].Id;

        _output.WriteLine("Invalid sensor number.");
        return null;
    }

    private double? ReadNumber(string prompt)
    {
        _output.Write(prompt);
        string? line = _input.ReadLine();
        if (double.TryParse(line?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) &&
            !double.IsNaN(value) && !double.IsInfinity(value))
            return value;

        _output.WriteLine("Invalid number.");
        return null;
    }

    private void Report(bool success, string successMessage, string failureMessage) =>
        _output.WriteLine(success ? successMessage : failureMessage);
}
