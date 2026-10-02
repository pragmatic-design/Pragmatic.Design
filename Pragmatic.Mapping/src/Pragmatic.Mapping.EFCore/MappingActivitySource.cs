using System.Diagnostics;

namespace Pragmatic.Mapping.EFCore;

/// <summary>
///     Shared ActivitySource for all Pragmatic.Mapping.EFCore telemetry.
/// </summary>
internal static class MappingActivitySource
{
    // A literal, as in every other Pragmatic diagnostics class. Reading
    // AssemblyInformationalVersion reflectively would make it the only ActivitySource in the
    // framework that cannot be built without System.Reflection — for a string the others
    // hard-code.
    internal static readonly ActivitySource Instance = new(MappingTelemetry.ActivitySourceName, "1.0.0");
}
