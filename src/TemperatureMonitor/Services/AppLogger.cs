namespace TemperatureMonitor.Services;

public enum LogLevel
{
    Info,
    Warning,
    Error
}

/// <summary>Destination for application log messages.</summary>
public interface IAppLogger : IDisposable
{
    void Log(LogLevel level, string message);
}

/// <summary>
/// Thread-safe logger that appends timestamped lines to a text file.
/// Several sensor threads log at the same time, so every write is locked.
/// </summary>
public sealed class FileLogger : IAppLogger
{
    private readonly object _lock = new();
    private StreamWriter? _writer;

    public string FilePath { get; }

    public FileLogger(string filePath)
    {
        FilePath = filePath;
        string? directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _writer = new StreamWriter(new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true
        };
    }

    public void Log(LogLevel level, string message)
    {
        lock (_lock)
        {
            // Ignore messages after shutdown instead of throwing.
            _writer?.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level.ToString().ToUpperInvariant()}] {message}");
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}

/// <summary>Keeps log lines in memory. Useful for tests and for showing recent alerts.</summary>
public sealed class InMemoryLogger : IAppLogger
{
    private readonly object _lock = new();
    private readonly List<string> _lines = new();

    public bool IsDisposed { get; private set; }

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_lock)
                return _lines.ToList();
        }
    }

    public void Log(LogLevel level, string message)
    {
        lock (_lock)
            _lines.Add($"[{level.ToString().ToUpperInvariant()}] {message}");
    }

    public void Dispose() => IsDisposed = true;
}
