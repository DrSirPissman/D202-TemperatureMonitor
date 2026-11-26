# Data-Centre Temperature Monitoring System

D202 Individual Development Project. A C# (.NET 8) console application that simulates several temperature
sensors in a data centre. Each sensor runs independently; every reading is validated, given a threshold status,
checked for anomalies, stored in SQLite and logged. Temperatures can be smoothed and analysed from a numeric menu.

> **AI assistance:** with the lecturer's approval (learning-support accommodation), the application code and tests
> were drafted with an AI assistant (Claude) and then reviewed, tested and corrected by me.

## Prerequisites

- Windows 10/11 (also runs on macOS/Linux)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 (17.8 or later) with the **.NET desktop development** workload, or the .NET CLI
- Git

No database server is needed. SQLite is embedded through the `Microsoft.Data.Sqlite` NuGet package.

## Installation

```powershell
git clone https://github.com/DrSirPissman/D202-TemperatureMonitor.git
cd D202-TemperatureMonitor
dotnet restore
```

In Visual Studio: **File → Clone Repository**, or open `TemperatureMonitor.sln`.

## Configuration

Sensors are defined in `src/TemperatureMonitor/sensors.json`, which is copied next to the executable on build.

```json
{
  "Id": "S1",
  "Name": "Rack A Inlet",
  "Location": "Row 1, Rack A",
  "MinTemp": -10,
  "MaxTemp": 60,
  "WarningThreshold": 26,
  "CriticalThreshold": 30
}
```

| Field | Rule |
|-------|------|
| `Id`, `Name`, `Location` | Required (not blank). `Id` must be unique. |
| `MinTemp`, `MaxTemp` | Physical limits of the sensor. `MinTemp` must be lower than `MaxTemp`. |
| `WarningThreshold`, `CriticalThreshold` | Warning must be lower than critical; both must be within the limits. |

Invalid sensors are rejected and logged, and the valid sensors still load. The supplied file contains a
deliberately misconfigured sensor (`S4`) to show this.

Thresholds changed from the menu (option 9) are saved back to `sensors.json` in the output folder, so they are
used the next time the application starts.

## Build and run

```powershell
dotnet build
dotnet run --project src/TemperatureMonitor
```

In Visual Studio: set **TemperatureMonitor** as the startup project and press **F5**.

## Application structure

```
TemperatureMonitor.sln
├── src/TemperatureMonitor/
│   ├── Program.cs                     Entry point: loads config, opens database and log, runs the menu
│   ├── sensors.json                   Sensor configuration
│   ├── Models/
│   │   ├── SensorConfig.cs            Sensor settings
│   │   ├── SensorReading.cs           One reading and its statuses
│   │   └── Statuses.cs                ThresholdStatus, AnomalyStatus, ValidationResult
│   ├── Services/
│   │   ├── SensorManager.cs           Sensor registry, reading loops and the processing pipeline
│   │   ├── TemperatureProcessor.cs    ValidateData, CheckThreshold, DetectAnomaly, SmoothData rules
│   │   ├── SensorConfigValidator.cs   Configuration and threshold validation
│   │   ├── TemperatureSimulator.cs    22–24 °C readings plus controlled abnormal values
│   │   ├── SqliteReadingRepository.cs SQLite storage and retrieval
│   │   ├── ConfigurationStore.cs      Load/save sensors.json
│   │   └── AppLogger.cs               Thread-safe file logger
│   └── UI/ConsoleMenu.cs              Numeric menu (testable through TextReader/TextWriter)
├── tests/TemperatureMonitor.Tests/    xUnit tests
├── evidence/                          Coverage report and screenshots
└── .github/workflows/ci.yml           Build + test + coverage on every push
```

### Required methods

| Brief | Implementation |
|-------|----------------|
| InitialiseSensor | `SensorManager.InitialiseSensor`, `InitialiseSensorsFromFile`, `InitialiseSensorsFromJson` |
| StartSensor / StartAllSensors | `SensorManager.StartSensor`, `StartAllSensors` |
| StopSensor / StopAllSensors | `SensorManager.StopSensor`, `StopAllSensors` |
| SimulateData | `SensorManager.SimulateData` (called repeatedly by each sensor's loop), `TemperatureSimulator` |
| ValidateData | `TemperatureProcessor.ValidateData` |
| CheckThreshold | `TemperatureProcessor.CheckThreshold` |
| DetectAnomaly | `TemperatureProcessor.DetectAnomaly` |
| SmoothData | `TemperatureProcessor.SmoothData`, `SensorManager.SmoothData` |
| LogData | `SensorManager.LogData` |
| StoreData / RetrieveData | `SensorManager.StoreData` / `RetrieveData`, `SqliteReadingRepository` |
| ResetSensor / DeleteSensor | `SensorManager.ResetSensor` / `DeleteSensor` |
| ExitApplication | `SensorManager.ExitApplication` |

## Usage

```
=== Data Centre Temperature Monitor ===
 1. Start a sensor                    8. Analyse temperatures for all sensors
 2. Start all sensors                 9. Configure alert thresholds
 3. Stop a sensor                    10. Reset a sensor
 4. Stop all sensors                 11. Reset all sensors
 5. View readings for a sensor       12. Delete a sensor (asks for confirmation)
 6. View readings for all sensors    13. Generate a controlled (abnormal) reading
 7. Analyse temperatures for a sensor 14. Show sensor status
 0. Exit
```

- Sensors are chosen by number from a list. Viewing readings (option 5) asks for a sensor ID instead, so the
  history of deleted sensors can still be viewed.
- Option 13 sends a chosen temperature through the full pipeline, for testing alerts and anomaly detection.
  For example, start sensor S1, wait for five readings, then generate 28 °C to see a Warning and an Anomaly.
- Invalid input (letters, out-of-range numbers, blank lines) shows a message and returns to the menu.
- Option 0 (or closing input / Ctrl+C) stops every sensor and closes the database and log before exiting.

## Main features and temperature-processing rules

| Rule | Behaviour |
|------|-----------|
| Simulation | Each running sensor produces a reading every 2 seconds: 23 °C ± 1 °C (22–24 °C), rounded to 2 decimals. |
| Controlled readings | A queued value replaces the next simulated reading for that sensor only. |
| Validation | Sensor ID, name, location and timestamp are required. The temperature must be a real number within the sensor's `MinTemp`–`MaxTemp` (inclusive). Invalid readings are logged and not stored or used in calculations. |
| Thresholds | `temperature >= CriticalThreshold` → **Critical**; `>= WarningThreshold` → **Warning**; otherwise **Normal**. |
| Anomaly detection | The latest valid reading is compared with the **average of the previous five valid readings** from the same sensor. A difference of **3 °C or more** (up or down) is an **Anomaly**. With fewer than five previous readings the result is **NotEvaluated**. |
| Smoothing | Average of the latest five valid readings, or of all available readings if fewer than five. |
| Independence | Each sensor has its own loop, in-memory history and lock. Starting, stopping, resetting or deleting one sensor does not affect the others. |
| Reset | Stops the sensor and clears its in-memory history (used for anomaly detection and smoothing). Configuration and stored readings are kept. |
| Delete | After confirmation, stops the sensor and removes it from the registry and `sensors.json`. Its stored readings stay in the database. |

## SQLite database

- File: `data/readings.db` next to the executable (e.g. `src/TemperatureMonitor/bin/Debug/net8.0/data/readings.db`).
- The table is created automatically if it does not exist, so data survives restarts, resets and deletions.

| Column | Type |
|--------|------|
| Id | INTEGER, primary key, auto-increment |
| SensorId, SensorName, Location | TEXT |
| Temperature | REAL |
| Timestamp | TEXT (ISO 8601) |
| ThresholdStatus | TEXT (Normal / Warning / Critical) |
| AnomalyStatus | TEXT (NotEvaluated / Normal / Anomaly) |

All queries use parameters. Each operation opens and closes its own connection (pooling off), so the file is
released when the application exits.

## Logging

- File: `logs/temperature-monitor.log` next to the executable. Lines are appended with a timestamp and level.
- Logged events: sensors loaded or rejected (with reasons), start/stop/reset/delete, every accepted reading,
  every rejected reading with its validation errors, Warning/Critical alerts, anomaly outcomes, threshold changes
  and shutdown.

```
2026-11-26 13:40:02.115 [INFO] ACCEPTED 2026-11-26 13:40:02 | S1 (Rack A Inlet, Row 1, Rack A) | 28.00 °C | Warning | Anomaly
2026-11-26 13:40:02.116 [WARNING] ALERT WARNING S1: 28.00 °C.
2026-11-26 13:40:02.116 [WARNING] ANOMALY S1: 28.00 °C differs by 3 °C or more from the previous five readings.
```

## Testing

```powershell
dotnet test
```

Or **Test → Run All Tests** in Visual Studio. The tests use in-memory fakes and temporary SQLite files, so they
never touch the real database.

| Test class | Covers |
|------------|--------|
| SensorConfigurationTests | Valid/invalid configs, missing fields, limits, thresholds, duplicates, malformed JSON, missing file, invalid sensors not blocking valid ones |
| SensorManagementTests | Start/stop/reset/delete for one and all sensors, duplicate loops prevented, restart, independence, threshold updates |
| AbnormalReadingTests | Controlled abnormal readings through the full pipeline: warning, critical, anomaly, rejection |
| TemperatureProcessingTests | Validation boundaries, threshold boundaries, anomaly detection (incl. insufficient history), smoothing with 0/1/3/5/7 readings, simulator range |
| SqliteStorageTests | Storage, retrieval, filtering, persistence across restart, reset and delete, concurrent writes |
| LoggingAndShutdownTests | Log file contents, concurrent logging, clean shutdown, database file released |
| ConsoleMenuTests | Every menu option, invalid input, delete confirmation, saving thresholds |
| ConfigurationStoreTests | Saving and loading sensors.json |

### Code coverage

```powershell
dotnet test --collect:"XPlat Code Coverage" --results-directory ./TestResults
dotnet tool install --global dotnet-reportgenerator-globaltool
reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:"evidence/coverage" -reporttypes:"Html;TextSummary"
```

GitHub Actions also generates the report on every push (download the `test-and-coverage-results` artifact from the
run). `Program.cs` is excluded from coverage because it only connects the real console and files.

**Result (CI run #24, 26 Nov 2026):** line coverage **98.3%** (586 of 596 lines), branch coverage **95.6%** (263 of 275), against a target of at least 70%. All tests pass. Summary: [`evidence/coverage-summary.md`](evidence/coverage-summary.md).

## GitHub development process

- `main`: stable, released code (tagged `v1.0` at submission)
- `develop`: integration branch
- `feature/...`: one branch per group of Kanban tasks, merged into `develop` through a pull request once CI passes
- GitHub Actions builds and runs all tests on every push and pull request

Kanban board: [D202 Temperature Monitor Kanban](https://github.com/users/DrSirPissman/projects/4)
(columns: To do → In progress → Testing/Review → Done).

## Known limitations

- Readings are simulated; there is no real sensor hardware.
- The reading interval (2 seconds) is fixed in `Program.cs`.
- "View readings" shows the latest 20 readings with a total count, not the full history.
- Sensors can't be added from the menu; add them to `sensors.json` and restart.
- Sensors removed from the menu are also removed from the saved `sensors.json`, including any rejected entries.
- Log and database files grow without limit; there is no rotation or archiving.
