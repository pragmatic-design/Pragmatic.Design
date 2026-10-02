using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Transforms;

/// <summary>
///     Versioned method parsing (Execute / HandleAsync conventions) and per-version body filtering.
/// </summary>
internal static partial class EndpointTransform
{
    /// <summary>
    ///     Extracts the version string from [SinceVersion] attribute, if present.
    /// </summary>
    private static string? GetSinceVersion(IPropertySymbol property)
    {
        var attr = property.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == EndpointAttributeNames.SinceVersion);

        if (attr?.ConstructorArguments.Length > 0)
            return attr.ConstructorArguments[0].Value?.ToString();

        return null;
    }

    /// <summary>
    ///     Regex for matching versioned Execute method names (DomainAction).
    ///     Supports: Execute, ExecuteV2, ExecuteV2_1, ExecuteV2_1_3
    /// </summary>
    private static readonly Regex VersionedExecuteRegex = new(@"^Execute(V(\d+)(_(\d+)(_(\d+))?)?)?$", RegexOptions.Compiled);

    /// <summary>
    ///     Regex for matching versioned HandleAsync method names (Endpoint&lt;T&gt;).
    ///     Supports: HandleAsync, HandleAsyncV2, HandleAsyncV2_1, HandleAsyncV2_1_3
    /// </summary>
    private static readonly Regex VersionedHandleAsyncRegex = new(@"^HandleAsync(V(\d+)(_(\d+)(_(\d+))?)?)?$", RegexOptions.Compiled);

    /// <summary>
    ///     Detects versioned Execute methods on a DomainAction and builds ActionVersionModel list.
    ///     Each version contains only the body properties available for that version.
    /// </summary>
    internal static ImmutableArray<ActionVersionModel> ParseActionVersions(
        INamedTypeSymbol symbol,
        ImmutableArray<BodyPropertyModel> bodyProperties)
        => ParseVersionedMethods(symbol, bodyProperties, VersionedExecuteRegex, "Execute");

    /// <summary>
    ///     Detects versioned HandleAsync methods on an Endpoint and builds ActionVersionModel list.
    ///     Each version contains only the body properties available for that version.
    /// </summary>
    internal static ImmutableArray<ActionVersionModel> ParseEndpointVersions(
        INamedTypeSymbol symbol,
        ImmutableArray<BodyPropertyModel> bodyProperties)
        => ParseVersionedMethods(symbol, bodyProperties, VersionedHandleAsyncRegex, "HandleAsync");

    /// <summary>
    ///     Shared versioning parser for both Execute (DomainAction) and HandleAsync (Endpoint) conventions.
    /// </summary>
    private static ImmutableArray<ActionVersionModel> ParseVersionedMethods(
        INamedTypeSymbol symbol,
        ImmutableArray<BodyPropertyModel> bodyProperties,
        Regex versionRegex,
        string baseMethodName)
    {
        var versions = new List<(int Major, int Minor, int Patch, string MethodName)>();
        var hasVersioned = false;

        foreach (var member in symbol.GetMembers().OfType<IMethodSymbol>())
        {
            var match = versionRegex.Match(member.Name);
            if (!match.Success)
                continue;

            // Verify method signature: must take CancellationToken
            if (member.Parameters.Length != 1)
                continue;
            if (member.Parameters[0].Type.ToDisplayString() != "System.Threading.CancellationToken")
                continue;

            if (match.Groups[1].Success)
            {
                var major = int.Parse(match.Groups[2].Value);
                var minor = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 0;
                var patch = match.Groups[6].Success ? int.Parse(match.Groups[6].Value) : 0;
                versions.Add((major, minor, patch, member.Name));
                hasVersioned = true;
            }
            // else: base method — we always include v1.0.0
        }

        if (!hasVersioned)
            return ImmutableArray<ActionVersionModel>.Empty;

        // Always include base method as v1.0
        versions.Insert(0, (1, 0, 0, baseMethodName));

        // Sort by version (major, minor, patch)
        versions.Sort((a, b) =>
        {
            var cmp = a.Major.CompareTo(b.Major);
            if (cmp != 0) return cmp;
            cmp = a.Minor.CompareTo(b.Minor);
            return cmp != 0 ? cmp : a.Patch.CompareTo(b.Patch);
        });

        // Build ActionVersionModels with filtered body properties
        var builder = ImmutableArray.CreateBuilder<ActionVersionModel>();
        foreach (var (major, minor, patch, methodName) in versions)
        {
            var versionBodyProps = FilterBodyPropertiesForVersion(bodyProperties, major, minor);
            builder.Add(new ActionVersionModel
            {
                Major = major,
                Minor = minor,
                Patch = patch,
                MethodName = methodName,
                BodyProperties = versionBodyProps
            });
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Filters body properties to include only those available for the given version.
    ///     Properties without [SinceVersion] are available since v1.0.
    /// </summary>
    private static ImmutableArray<BodyPropertyModel> FilterBodyPropertiesForVersion(
        ImmutableArray<BodyPropertyModel> allProperties, int major, int minor)
    {
        return allProperties
            .Where(p =>
            {
                if (p.SinceVersion is null)
                    return true; // Available since v1.0

                var parts = p.SinceVersion.Split('.');
                var propMajor = int.Parse(parts[0]);
                var propMinor = parts.Length > 1 ? int.Parse(parts[1]) : 0;

                // Property is available if its version <= requested version
                return propMajor < major || (propMajor == major && propMinor <= minor);
            })
            .ToImmutableArray();
    }
}
