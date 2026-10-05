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

        // No CommandLine: it is where secrets travel (a connection string, a token passed as an
        // argument), and every entry would copy them into every sink, beyond the reach of declared
        // redaction. An application that wants it adds it through a context provider of its own.
        return CreatePropertiesDictionary(
            ("ProcessId", process.Id),
            ("ProcessName", process.ProcessName),
            ("ApplicationName", assembly.GetName().Name),
            ("ApplicationVersion", assembly.GetName().Version?.ToString()),
            ("StartTime", process.StartTime),
            ("WorkingDirectory", Environment.CurrentDirectory)
        );
    }
}