using Microsoft.Extensions.Logging;

namespace Pragmatic.SourceGenerator.Tests.Features.Logging;

/// <summary>A logger that keeps the last call it was given, state and formatter output included.</summary>
internal sealed class CapturingLogger(LogLevel minimum = LogLevel.Trace) : ILogger
{
    public int Calls { get; private set; }
    public LogLevel Level { get; private set; }
    public EventId EventId { get; private set; }
    public object? State { get; private set; }
    public Exception? Exception { get; private set; }
    public string? Message { get; private set; }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= minimum;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Calls++;
        Level = logLevel;
        EventId = eventId;
        State = state;
        Exception = exception;
        Message = formatter(state, exception);
    }
}
