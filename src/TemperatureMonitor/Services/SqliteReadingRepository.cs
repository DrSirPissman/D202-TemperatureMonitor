using System.Globalization;
using Microsoft.Data.Sqlite;
using TemperatureMonitor.Models;

namespace TemperatureMonitor.Services;

/// <summary>Persistent storage for accepted readings.</summary>
public interface IReadingRepository : IDisposable
{
    void Store(SensorReading reading);

    /// <summary>Returns readings for one sensor, or every sensor when <paramref name="sensorId"/> is null. Oldest first.</summary>
    IReadOnlyList<SensorReading> Retrieve(string? sensorId = null);
}

/// <summary>
/// Stores validated readings in an SQLite database file. The table is created if it does not exist,
/// so readings survive application restarts, sensor resets and sensor deletion.
/// </summary>
public sealed class SqliteReadingRepository : IReadingRepository
{
    private readonly object _lock = new();
    private readonly string _connectionString;
    private bool _disposed;

    public string DatabasePath { get; }

    public SqliteReadingRepository(string databasePath)
    {
        DatabasePath = databasePath;
        string? directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();

        CreateTable();
    }

    private void CreateTable()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Readings (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                SensorId        TEXT NOT NULL,
                SensorName      TEXT NOT NULL,
                Location        TEXT NOT NULL,
                Temperature     REAL NOT NULL,
                Timestamp       TEXT NOT NULL,
                ThresholdStatus TEXT NOT NULL,
                AnomalyStatus   TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Readings_SensorId ON Readings (SensorId);
            """;
        command.ExecuteNonQuery();
    }

    public void Store(SensorReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        lock (_lock)
        {
            ThrowIfDisposed();
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Readings (SensorId, SensorName, Location, Temperature, Timestamp, ThresholdStatus, AnomalyStatus)
                VALUES ($sensorId, $sensorName, $location, $temperature, $timestamp, $threshold, $anomaly);
                """;
            command.Parameters.AddWithValue("$sensorId", reading.SensorId ?? string.Empty);
            command.Parameters.AddWithValue("$sensorName", reading.SensorName ?? string.Empty);
            command.Parameters.AddWithValue("$location", reading.Location ?? string.Empty);
            command.Parameters.AddWithValue("$temperature", reading.Temperature);
            command.Parameters.AddWithValue("$timestamp", reading.Timestamp.ToString("o", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$threshold", reading.ThresholdStatus.ToString());
            command.Parameters.AddWithValue("$anomaly", reading.AnomalyStatus.ToString());
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<SensorReading> Retrieve(string? sensorId = null)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = sensorId == null
                ? "SELECT SensorId, SensorName, Location, Temperature, Timestamp, ThresholdStatus, AnomalyStatus FROM Readings ORDER BY Id;"
                : "SELECT SensorId, SensorName, Location, Temperature, Timestamp, ThresholdStatus, AnomalyStatus FROM Readings WHERE SensorId = $sensorId ORDER BY Id;";
            if (sensorId != null)
                command.Parameters.AddWithValue("$sensorId", sensorId);

            var readings = new List<SensorReading>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                readings.Add(new SensorReading
                {
                    SensorId = reader.GetString(0),
                    SensorName = reader.GetString(1),
                    Location = reader.GetString(2),
                    Temperature = reader.GetDouble(3),
                    Timestamp = DateTime.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    ThresholdStatus = Enum.Parse<ThresholdStatus>(reader.GetString(5)),
                    AnomalyStatus = Enum.Parse<AnomalyStatus>(reader.GetString(6))
                });
            }
            return readings;
        }
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SqliteReadingRepository));
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
        }
    }
}
