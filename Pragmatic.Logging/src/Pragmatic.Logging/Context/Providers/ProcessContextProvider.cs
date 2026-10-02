using System.Diagnostics;
using System.Reflection;

namespace Pragmatic.Logging.Context.Providers;

/// <summary>
/// Provides process-level context information.
/// </summary>
public sealed class ProcessContextProvider() : ContextProviderBase("Process", priority: 900)
{
    private static readonly Lazy<IReadOnlyDictionary<string, object?>> _cachedProperties =
        new(CreateProcessProperties);

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, object?> GetContextProperties()
    {
        return _cachedProperties.Value;
    }

    private static IReadOnlyDictionary<string, object?> CreateProcessProperties()
    {
        var process = Process.GetCurrentProcess();

        // Cold-path startup metadata only: this runs once (memoized by the static Lazy above) to
        // capture the entry-assembly name/version for log enrichment. It is NOT the forbidden
        // runtime member-reflection (no GetProperty/GetMethods/Activator) — only assembly identity,
        // which has no source-generated equivalent. Resolved once, never on the logging hot path.
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

        return CreatePropertiesDictionary(
            ("ProcessId", process.Id),
            ("ProcessName", process.ProcessName),
            ("ApplicationName", assembly.GetName().Name),
            ("ApplicationVersion", assembly.GetName().Version?.ToString()),
            ("StartTime", process.StartTime),
            ("WorkingDirectory", Environment.CurrentDirectory),
            ("CommandLine", Environment.CommandLine)
        );
    }
}