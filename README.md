# Data-Centre Temperature Monitoring System

D202 Individual Development Project – C# (.NET 8) console application that simulates
multiple temperature sensors in a data centre, validates readings, raises threshold
alerts, detects anomalies, smooths temperatures and stores accepted readings in SQLite.

> **Status:** project scaffold only. Sections marked _TODO_ are filled in as features are built.

## Project overview

_TODO: short description of what the application does and who it is for._

## Prerequisites

- Windows 10/11, macOS or Linux
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 (17.8 or later) with the **.NET desktop development** workload, or any editor plus the .NET CLI
- Git

## Installation

```powershell
git clone <repository-url>
cd <repository-folder>
dotnet restore
```

## Configuration

_TODO: describe the sensor configuration file (sensor ID, name, location, min/max limits,
warning and critical thresholds) and where threshold settings are saved._

## Build and run

```powershell
dotnet build
dotnet run --project src/TemperatureMonitor
```

In Visual Studio: open `TemperatureMonitor.sln`, set **TemperatureMonitor** as the startup project and press **F5**.

## Application structure

```
TemperatureMonitor.sln
├── src/TemperatureMonitor/            Console application
├── tests/TemperatureMonitor.Tests/    xUnit test project
├── evidence/                          Coverage reports and screenshots
├── docs/                              Development notes
└── .github/workflows/ci.yml           GitHub Actions build + test pipeline
```

_TODO: list the main classes and what each is responsible for._

## Usage

_TODO: describe the numeric menu options (start/stop, view, analyse, configure thresholds,
reset, delete, exit) with example output._

## Main features and temperature-processing rules

| Rule | Behaviour |
|------|-----------|
| Simulation | Normal readings around 22–24 °C with small random variation; controlled abnormal readings for testing |
| Validation | _TODO: required fields and min/max limits; invalid readings rejected from storage_ |
| Thresholds | _TODO: Normal / Warning / Critical boundaries_ |
| Anomaly detection | Latest valid reading compared with the previous five valid readings from the same sensor; difference ≥ 3 °C is flagged; `NotEvaluated` when there is not enough history |
| Smoothing | Average of the latest five valid readings (or all available if fewer than five) |
| Reset | Stops the sensor and clears in-memory history; configuration and stored readings kept |
| Delete | Stops and unregisters the sensor (with confirmation); historical readings kept |

## SQLite database

_TODO: database file location, table schema (sensor ID, name, location, temperature,
timestamp, threshold status, anomaly status) and how persistence works across restarts._

## Logging

_TODO: log file location and what is logged (accepted/rejected readings, validation errors,
alerts, anomaly outcomes)._

## Testing

```powershell
dotnet test
```

Code coverage:

```powershell
dotnet test --collect:"XPlat Code Coverage" --results-directory ./TestResults
reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:"evidence/coverage" -reporttypes:"Html;TextSummary"
```

_TODO: coverage result (target ≥ 70%) and links to evidence in `evidence/`._

## GitHub development process

- `main` – stable, released code (tagged `v1.0` at submission)
- `develop` – integration branch
- `feature/<issue-number>-<short-name>` – one branch per Kanban task, merged into `develop` by pull request
- GitHub Actions builds and runs the tests on every push and pull request

Kanban board: _TODO: add link_

## Known limitations

_TODO_
