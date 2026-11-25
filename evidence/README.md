# Evidence

Put assessment evidence in this folder, for example:

- `coverage/` – code-coverage report (HTML from ReportGenerator, or a screenshot of the summary). Must show at least 70% line coverage of the application code.
- `github-actions-passing.png` – screenshot of a green CI run for the submitted version.
- `kanban-board.png` – screenshots of the Kanban board at different stages.
- `tests-passing.png` – screenshot of Test Explorer / `dotnet test` output.
- `app-running.png` – screenshots of the console menu working.

## Generating a coverage report locally

```powershell
dotnet test --collect:"XPlat Code Coverage" --results-directory ./TestResults
dotnet tool install --global dotnet-reportgenerator-globaltool
reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:"evidence/coverage" -reporttypes:"Html;TextSummary"
```

The CI workflow also produces this report on every push: open the run in the
**Actions** tab and download the `test-and-coverage-results` artifact.
