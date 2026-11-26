using System.Diagnostics.CodeAnalysis;
using System.Text;
using TemperatureMonitor.Services;
using TemperatureMonitor.UI;

namespace TemperatureMonitor;

/// <summary>
/// Entry point: loads sensors.json, opens the SQLite database and log file, then runs the menu.
/// Excluded from coverage because it only wires real files and the real console together.
/// </summary>
[ExcludeFromCodeCoverage]
public static class Program
{
    public static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        string baseDir = AppContext.BaseDirectory;
        string configPath = Path.Combine(baseDir, "sensors.json");
        string databasePath = Path.Combine(baseDir, "data", "readings.db");
        string logPath = Path.Combine(baseDir, "logs", "temperature-monitor.log");

        var logger = new FileLogger(logPath);
        var repository = new SqliteReadingRepository(databasePath);
        var manager = new SensorManager(repository, logger, new TemperatureSimulator(), TimeSpan.FromSeconds(2));

        // Make sure sensors stop and files close even if the window is closed with Ctrl+C.
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            manager.ExitApplication();
            Environment.Exit(0);
        };

        var result = manager.InitialiseSensorsFromFile(configPath);
        Console.WriteLine($"Loaded {result.Loaded.Count} sensor(s) from {configPath}.");
        foreach (var (sensorId, errors) in result.Rejected)
            Console.WriteLine($"  Rejected {sensorId}: {string.Join(" ", errors)}");
        Console.WriteLine($"Database: {databasePath}");
        Console.WriteLine($"Log file: {logPath}");

        new ConsoleMenu(manager, Console.In, Console.Out, configPath).Run();
    }
}
