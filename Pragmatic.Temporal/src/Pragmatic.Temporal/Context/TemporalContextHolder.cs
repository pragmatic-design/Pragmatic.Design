namespace Pragmatic.Temporal.Context;

/// <summary>
///     Ambient access to the current <see cref="TemporalContext" /> for components that
///     cannot take it via DI — most notably JSON serialization, whose converters and
///     type-info modifiers are configured once per application but must honor the
///     per-request client/business timezones.
/// </summary>
/// <remarks>
///     Backed by <see cref="AsyncLocal{T}" />, so the value flows with the async execution
///     context and never leaks across concurrent requests. The ASP.NET Core middleware sets
///     it at the start of each request and clears it when the request completes. Outside a
///     request (background jobs, tests) it is null unless set explicitly.
/// </remarks>
public static class TemporalContextHolder
{
    private static readonly AsyncLocal<TemporalContext?> Ambient = new();

    /// <summary>Gets or sets the temporal context for the current async flow.</summary>
    public static TemporalContext? Current
    {
        get => Ambient.Value;
        set => Ambient.Value = value;
    }
}
