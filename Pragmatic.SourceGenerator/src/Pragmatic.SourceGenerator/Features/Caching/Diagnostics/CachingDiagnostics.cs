using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Caching.Diagnostics;

/// <summary>Diagnostic descriptors for Caching feature. Range: PRAG1700-PRAG1799</summary>
internal static class CachingDiagnostics
{
    // PRAG1700 ([Cacheable] type must be partial) is the companion analyzer's, which reports it on the
    // declaration (NotPartialDiagnosticDescriptors); the generator skips the type silently.

    // Warning, not Error: an invalid duration is recoverable — the generator falls back to a 5-minute
    // default and still emits valid ICacheable code, so it must not fail the build.
    public static readonly DiagnosticDescriptor InvalidDuration = DiagnosticFactory.Warning(
        "PRAG1701", "Invalid cache duration",
        "Invalid cache duration format '{0}'. Use formats like '5m', '1h', '1d' or a valid TimeSpan string",
        "Duration must be in format: Nm (minutes), Nh (hours), Nd (days), or TimeSpan format.");

    public static readonly DiagnosticDescriptor NoKeyProperties = DiagnosticFactory.Error(
        "PRAG1702", "No cache key properties",
        "Type '{0}' has no properties to generate cache key. Add at least one public property or remove [Cacheable]",
        "Cache key generation requires at least one public property not marked with [CacheKey(Exclude = true)].");

    public static readonly DiagnosticDescriptor InvalidPlaceholder = DiagnosticFactory.Error(
        "PRAG1703", "Invalid placeholder",
        "Placeholder '{{{0}}}' in tag/key refers to non-existent property on type '{1}'",
        "Ensure the placeholder name matches an existing public property on the type.");

    public static readonly DiagnosticDescriptor DuplicateOrder = DiagnosticFactory.Warning(
        "PRAG1750", "Duplicate cache key order",
        "Properties '{0}' and '{1}' have the same Order value {2}. Order may be non-deterministic",
        "Assign unique Order values to control the order of properties in the cache key.");

    public static readonly DiagnosticDescriptor AllPropertiesExcluded = DiagnosticFactory.Warning(
        "PRAG1751", "All properties excluded from cache key",
        "All properties on type '{0}' are excluded from cache key generation. This may cause cache collisions",
        "Consider keeping at least one identifying property in the cache key.");

    /// <summary>
    ///     A complex property the key walk could not read to the bottom — too deep, or a type that
    ///     refers back to itself.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Reported rather than tolerated because the consequence is the defect this walk removes:
    ///     the fragments that were reached still differentiate, the ones beyond the limit do not, and
    ///     two requests differing only past that point share a cache entry. Silence here would put back
    ///     the collision it was written to remove, one level deeper.
    /// </remarks>
    public static readonly DiagnosticDescriptor KeyPropertyNotFullyRead = DiagnosticFactory.Warning(
        "PRAG1705", "Cache key cannot read a complex property to the bottom",
        "Property '{0}' on type '{1}' nests deeper than the cache key walks, or refers to itself. "
        + "Two requests differing only below that point will share a cache entry",
        "Flatten the property, exclude it with [CacheKey(Exclude = true)] if it does not affect the "
        + "result, or split the query.");

    public static readonly DiagnosticDescriptor InvalidatesMustBePartial = DiagnosticFactory.Error(
        "PRAG1704", "Type must be partial",
        "Type '{0}' must be declared as partial to use [InvalidatesCache]",
        "Add the 'partial' keyword to the type declaration.");
}
