namespace Pragmatic.Agent;

/// <summary>
///     Simple logging for the Agent daemon. Writes to Console (structured format).
///     Replaceable: set <see cref="Output"/> to redirect to a file or custom sink.
/// </summary>
internal static class AgentLogger
{
    /// <summary>Log output writer. Default: Console.Out. Override for file/custom sinks.</summary>
    public static TextWriter Output { get; set; } = Console.Out;

    /// <summary>Error output writer. Default: Console.Error.</summary>
    public static TextWriter ErrorOutput { get; set; } = Console.Error;

    /// <summary>Minimum log level. Default: Info.</summary>
    public static LogLevel MinLevel { get; set; } = LogLevel.Info;

    public static void Debug(string category, string message)
    {
        if (MinLevel <= LogLevel.Debug)
            Write("DBG", category, message);
    }

    public static void Info(string category, string message)
    {
        if (MinLevel <= LogLevel.Info)
            Write("INF", category, message);
    }

    public static void Warn(string category, string message)
    {
        if (MinLevel <= LogLevel.Warn)
            Write("WRN", category, message, error: true);
    }

    public static void Error(string category, string message, Exception? ex = null)
    {
        if (MinLevel <= LogLevel.Error)
        {
            Write("ERR", category, message, error: true);
            if (ex is not null)
                ErrorOutput.WriteLine($"  {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Write(string level, string category, string message, bool error = false)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("HH:mm:ss.fff");
        var line = $"[{timestamp}] [{level}] [{category}] {message}";
        (error ? ErrorOutput : Output).WriteLine(line);
    }

    internal enum LogLevel { Debug, Info, Warn, Error }
}
