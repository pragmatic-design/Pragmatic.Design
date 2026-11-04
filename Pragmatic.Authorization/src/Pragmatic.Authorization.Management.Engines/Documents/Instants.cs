namespace Pragmatic.Authorization.Management.Engines.Documents;

/// <summary>The documents' instants: UTC ticks, which sort and compare as the instants do.</summary>
internal static class Instants
{
    public static DateTimeOffset Of(long ticks) => new(ticks, TimeSpan.Zero);

    public static DateTimeOffset? Of(long? ticks) => ticks is { } value ? Of(value) : null;
}
