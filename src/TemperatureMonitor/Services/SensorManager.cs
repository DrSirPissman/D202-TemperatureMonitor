using System.Text.Json;
using TemperatureMonitor.Models;

namespace TemperatureMonitor.Services;

/// <summary>Outcome of loading sensor configurations.</summary>
public class InitialisationResult
{
    public List<SensorConfig> Loaded { get; } = new();
    public List<(string SensorId, List<string> Errors)> Rejected { get; } = new();
}

/// <summary>
/// Registers sensors and runs each one on its own independent reading loop.
/// Every reading goes through: ValidateData → CheckThreshold → DetectAnomaly → StoreData → LogData.
/// </summary>
public sealed class SensorManager : IDisposable
{
    /// <summary>Maximum number of valid readings kept in memory per sensor.</summary>
    public const int MaxHistory = 100;

    private sealed class SensorState
    {
        public SensorState(SensorConfig config) => Config = config;

        public SensorConfig Config { get; set; }
        public List<double> History { get; } = new();
        public object Sync { get; } = new();
        public CancellationTokenSource? Cancellation { get; set; }
        public Task? Loop { get; set; }
        public bool IsRunning => Loop != null && !Loop.IsCompleted;
    }

    private readonly Dictionary<string, SensorState> _sensors = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _registryLock = new();
    private readonly IReadingRepository _repository;
    private readonly IAppLogger _logger;
    private readonly TemperatureSimulator _simulator;
    private readonly TimeSpan _interval;

    public bool IsShutDown { get; private set; }

    public SensorManager(IReadingRepository repository, IAppLogger logger, TemperatureSimulator simulator, TimeSpan readingInterval)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _simulator = simulator ?? throw new ArgumentNullException(nameof(simulator));
        _interval = readingInterval;
    }

    // ------------------------------------------------------------------ configuration

    /// <summary>
    /// InitialiseSensor: validates and registers each configuration. Invalid or duplicate sensors are
    /// rejected and logged; valid sensors are still registered.
    /// </summary>
    public InitialisationResult InitialiseSensor(IEnumerable<SensorConfig?> configurations)
    {
        var result = new InitialisationResult();

        foreach (var config in configurations)
        {
            var validation = SensorConfigValidator.Validate(config);
            string id = config?.Id ?? "(no id)";

            if (validation.IsValid)
            {
                lock (_registryLock)
                {
                    if (_sensors.ContainsKey(config!.Id!))
                        validation.Errors.Add($"Duplicate sensor ID '{config.Id}'.");
                    else
                        _sensors[config.Id!] = new SensorState(config.Clone());
                }
            }

            if (validation.IsValid)
            {
                result.Loaded.Add(config!);
                _logger.Log(LogLevel.Info, $"Sensor {id} initialised ({config!.Name}, {config.Location}).");
            }
            else
            {
                result.Rejected.Add((id, validation.Errors));
                _logger.Log(LogLevel.Error, $"Sensor {id} rejected: {string.Join(" ", validation.Errors)}");
            }
        }

        return result;
    }

    /// <summary>Loads sensors from a JSON string. Malformed JSON is logged and loads nothing.</summary>
    public InitialisationResult InitialiseSensorsFromJson(string json)
    {
        try
        {
            return InitialiseSensor(ConfigurationStore.ParseJson(json));
        }
        catch (JsonException ex)
        {
            _logger.Log(LogLevel.Error, $"Sensor configuration is not valid JSON: {ex.Message}");
            return new InitialisationResult();
        }
    }

    /// <summary>Loads sensors from a JSON file. A missing file is logged and loads nothing.</summary>
    public InitialisationResult InitialiseSensorsFromFile(string path)
    {
        if (!File.Exists(path))
        {
            _logger.Log(LogLevel.Error, $"Sensor configuration file not found: {path}");
            return new InitialisationResult();
        }
        return InitialiseSensorsFromJson(File.ReadAllText(path));
    }

    /// <summary>Changes a sensor's warning and critical thresholds after validating them.</summary>
    public ValidationResult UpdateThresholds(string sensorId, double warning, double critical)
    {
        var state = Find(sensorId);
        if (state == null)
        {
            var notFound = new ValidationResult();
            notFound.Errors.Add($"Sensor '{sensorId}' is not registered.");
            return notFound;
        }

        lock (state.Sync)
        {
            var result = SensorConfigValidator.ValidateThresholds(state.Config, warning, critical);
            if (result.IsValid)
            {
                var updated = state.Config.Clone();
                updated.WarningThreshold = warning;
                updated.CriticalThreshold = critical;
                state.Config = updated;
                _logger.Log(LogLevel.Info, $"Sensor {sensorId} thresholds changed to warning {warning} °C, critical {critical} °C.");
            }
            return result;
        }
    }

    /// <summary>Saves the configuration of all registered sensors (including threshold changes) to a file.</summary>
    public void SaveConfiguration(string path)
    {
        ConfigurationStore.Save(path, GetSensors());
        _logger.Log(LogLevel.Info, $"Sensor configuration saved to {path}.");
    }

    // ------------------------------------------------------------------ queries

    public IReadOnlyList<SensorConfig> GetSensors()
    {
        lock (_registryLock)
            return _sensors.Values.Select(s => s.Config.Clone()).OrderBy(c => c.Id).ToList();
    }

    public bool IsRegistered(string sensorId) => Find(sensorId) != null;

    public bool IsRunning(string sensorId) => Find(sensorId)?.IsRunning ?? false;

    /// <summary>Copy of the in-memory valid temperatures for a sensor, oldest first.</summary>
    public IReadOnlyList<double> GetHistory(string sensorId)
    {
        var state = Find(sensorId);
        if (state == null)
            return Array.Empty<double>();
        lock (state.Sync)
            return state.History.ToList();
    }

    // ------------------------------------------------------------------ start / stop

    /// <summary>StartSensor: starts one sensor's reading loop. Returns false if unknown or already running.</summary>
    public bool StartSensor(string sensorId)
    {
        ThrowIfShutDown();
        var state = Find(sensorId);
        if (state == null)
        {
            _logger.Log(LogLevel.Warning, $"Cannot start sensor {sensorId}: not registered.");
            return false;
        }

        lock (state.Sync)
        {
            if (state.IsRunning)
            {
                _logger.Log(LogLevel.Warning, $"Sensor {sensorId} is already running.");
                return false;
            }

            var cancellation = new CancellationTokenSource();
            state.Cancellation = cancellation;
            state.Loop = Task.Run(() => RunLoopAsync(state.Config.Id!, cancellation.Token));
        }

        _logger.Log(LogLevel.Info, $"Sensor {sensorId} started.");
        return true;
    }

    /// <summary>StartAllSensors: starts every registered sensor that is not already running. Returns how many started.</summary>
    public int StartAllSensors() => GetSensors().Count(c => !IsRunning(c.Id!) && StartSensor(c.Id!));

    /// <summary>StopSensor: stops one sensor without affecting others. Data is kept and the sensor can be restarted.</summary>
    public bool StopSensor(string sensorId)
    {
        var state = Find(sensorId);
        if (state == null || !state.IsRunning)
            return false;

        StopState(state);
        _logger.Log(LogLevel.Info, $"Sensor {sensorId} stopped.");
        return true;
    }

    /// <summary>StopAllSensors: stops every running sensor without deleting any data. Returns how many stopped.</summary>
    public int StopAllSensors() => GetSensors().Count(c => StopSensor(c.Id!));

    private static void StopState(SensorState state)
    {
        CancellationTokenSource? cancellation;
        Task? loop;
        lock (state.Sync)
        {
            cancellation = state.Cancellation;
            loop = state.Loop;
            state.Cancellation = null;
        }

        cancellation?.Cancel();
        try
        {
            loop?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // The loop ended because it was cancelled; nothing else to do.
        }
        cancellation?.Dispose();
    }

    private async Task RunLoopAsync(string sensorId, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                SimulateData(sensorId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.Log(LogLevel.Error, $"Sensor {sensorId} reading failed: {ex.Message}");
            }

            try
            {
                await Task.Delay(_interval, token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    // ------------------------------------------------------------------ readings

    /// <summary>
    /// SimulateData: generates one reading (identity, location, temperature, timestamp) for a sensor and processes it.
    /// The running loop calls this repeatedly. Returns the accepted reading, or null if it was rejected.
    /// </summary>
    public SensorReading? SimulateData(string sensorId)
    {
        var state = Find(sensorId);
        if (state == null)
            return null;

        SensorConfig config;
        lock (state.Sync)
            config = state.Config;

        var reading = new SensorReading
        {
            SensorId = config.Id,
            SensorName = config.Name,
            Location = config.Location,
            Temperature = _simulator.NextTemperature(config.Id!),
            Timestamp = DateTime.Now
        };

        return ProcessReading(reading);
    }

    /// <summary>
    /// Queues a controlled abnormal temperature for a sensor and processes it immediately,
    /// so alerts and anomaly detection can be tested on demand.
    /// </summary>
    public SensorReading? InjectAbnormalReading(string sensorId, double temperature)
    {
        if (!IsRegistered(sensorId))
            return null;
        _simulator.QueueAbnormalReading(sensorId, temperature);
        _logger.Log(LogLevel.Info, $"Controlled reading of {temperature} °C queued for sensor {sensorId}.");
        return SimulateData(sensorId);
    }

    /// <summary>Runs a reading through validation, threshold check, anomaly detection, storage and logging.</summary>
    public SensorReading? ProcessReading(SensorReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        var state = reading.SensorId == null ? null : Find(reading.SensorId);
        if (state == null)
        {
            LogData(reading, accepted: false, new[] { $"Sensor '{reading.SensorId}' is not registered." });
            return null;
        }

        lock (state.Sync)
        {
            var validation = ValidateData(reading, state.Config);
            if (!validation.IsValid)
            {
                LogData(reading, accepted: false, validation.Errors);
                return null;
            }

            reading.ThresholdStatus = CheckThreshold(reading, state.Config);
            reading.AnomalyStatus = TemperatureProcessor.DetectAnomaly(reading.Temperature, state.History);

            state.History.Add(reading.Temperature);
            if (state.History.Count > MaxHistory)
                state.History.RemoveAt(0);
        }

        StoreData(reading);
        LogData(reading, accepted: true, Array.Empty<string>());
        return reading;
    }

    /// <summary>ValidateData: required fields and temperature limits.</summary>
    public static ValidationResult ValidateData(SensorReading reading, SensorConfig config) =>
        TemperatureProcessor.ValidateData(reading, config);

    /// <summary>CheckThreshold: Normal, Warning or Critical using the sensor's thresholds.</summary>
    public static ThresholdStatus CheckThreshold(SensorReading reading, SensorConfig config) =>
        TemperatureProcessor.CheckThreshold(reading.Temperature, config);

    /// <summary>DetectAnomaly for a sensor's next reading, using its in-memory history.</summary>
    public AnomalyStatus DetectAnomaly(SensorReading reading)
    {
        var history = reading.SensorId == null ? Array.Empty<double>() : GetHistory(reading.SensorId);
        return TemperatureProcessor.DetectAnomaly(reading.Temperature, history);
    }

    /// <summary>SmoothData: average of the sensor's latest five valid readings (or all if fewer). Null if none.</summary>
    public double? SmoothData(string sensorId) => TemperatureProcessor.SmoothData(GetHistory(sensorId));

    /// <summary>StoreData: saves an accepted reading in SQLite.</summary>
    public void StoreData(SensorReading reading) => _repository.Store(reading);

    /// <summary>RetrieveData: stored readings for one sensor, or all sensors when sensorId is null (includes deleted sensors).</summary>
    public IReadOnlyList<SensorReading> RetrieveData(string? sensorId = null) => _repository.Retrieve(sensorId);

    /// <summary>LogData: records accepted/rejected readings, validation errors, alerts and anomaly outcomes.</summary>
    public void LogData(SensorReading reading, bool accepted, IEnumerable<string> errors)
    {
        if (!accepted)
        {
            _logger.Log(LogLevel.Warning,
                $"REJECTED {reading.SensorId}: {reading.Temperature:F2} °C at {reading.Timestamp:HH:mm:ss}. {string.Join(" ", errors)}");
            return;
        }

        _logger.Log(LogLevel.Info, $"ACCEPTED {reading}");

        if (reading.ThresholdStatus == ThresholdStatus.Warning)
            _logger.Log(LogLevel.Warning, $"ALERT WARNING {reading.SensorId}: {reading.Temperature:F2} °C.");
        else if (reading.ThresholdStatus == ThresholdStatus.Critical)
            _logger.Log(LogLevel.Error, $"ALERT CRITICAL {reading.SensorId}: {reading.Temperature:F2} °C.");

        if (reading.AnomalyStatus == AnomalyStatus.Anomaly)
            _logger.Log(LogLevel.Warning, $"ANOMALY {reading.SensorId}: {reading.Temperature:F2} °C differs by 3 °C or more from the previous five readings.");
        else
            _logger.Log(LogLevel.Info, $"Anomaly check {reading.SensorId}: {reading.AnomalyStatus}.");
    }

    // ------------------------------------------------------------------ reset / delete / exit

    /// <summary>ResetSensor: stops the sensor and clears its in-memory history. Configuration and stored readings are kept.</summary>
    public bool ResetSensor(string sensorId)
    {
        var state = Find(sensorId);
        if (state == null)
            return false;

        StopState(state);
        lock (state.Sync)
            state.History.Clear();
        _simulator.ClearQueue(state.Config.Id!);

        _logger.Log(LogLevel.Info, $"Sensor {sensorId} reset (in-memory history cleared; stored readings kept).");
        return true;
    }

    /// <summary>Resets every registered sensor. Returns how many were reset.</summary>
    public int ResetAllSensors() => GetSensors().Count(c => ResetSensor(c.Id!));

    /// <summary>
    /// DeleteSensor: stops the sensor and removes it from active registration. Its stored readings stay in SQLite.
    /// The console menu asks the user to confirm before calling this.
    /// </summary>
    public bool DeleteSensor(string sensorId)
    {
        var state = Find(sensorId);
        if (state == null)
            return false;

        StopState(state);
        lock (_registryLock)
            _sensors.Remove(sensorId);
        _simulator.ClearQueue(state.Config.Id!);

        _logger.Log(LogLevel.Info, $"Sensor {sensorId} deleted (historical readings kept).");
        return true;
    }

    /// <summary>ExitApplication: stops all sensors and closes the database and log. Safe to call more than once.</summary>
    public void ExitApplication()
    {
        if (IsShutDown)
            return;

        int stopped = StopAllSensors();
        _logger.Log(LogLevel.Info, $"Application exiting. {stopped} running sensor(s) stopped.");
        _repository.Dispose();
        _logger.Dispose();
        IsShutDown = true;
    }

    public void Dispose() => ExitApplication();

    // ------------------------------------------------------------------ helpers

    private SensorState? Find(string sensorId)
    {
        if (string.IsNullOrWhiteSpace(sensorId))
            return null;
        lock (_registryLock)
            return _sensors.TryGetValue(sensorId, out var state) ? state : null;
    }

    private void ThrowIfShutDown()
    {
        if (IsShutDown)
            throw new InvalidOperationException("The application has been shut down.");
    }
}
