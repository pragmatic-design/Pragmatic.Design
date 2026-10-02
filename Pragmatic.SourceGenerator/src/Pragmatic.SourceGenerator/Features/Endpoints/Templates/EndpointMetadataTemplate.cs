using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     Generates assembly-level [PragmaticMetadata(MetadataCategory.Endpoints, ...)] attribute
///     with enriched JSON containing endpoint types, groups, and route information.
///     This enables the host to generate route mapping directly from metadata.
/// </summary>
internal sealed class EndpointMetadataTemplate : CSharpTemplate
{
    private readonly ImmutableArray<EndpointModel> _endpoints;
    private readonly bool _indent;

    public EndpointMetadataTemplate(ImmutableArray<EndpointModel> endpoints, bool indent)
    {
        _endpoints = endpoints;
        _indent = indent;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Endpoints";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForMetadata("Endpoints"),
        ToSourceText());

    protected override bool Validate() => !_endpoints.IsEmpty;

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");

        AppendLine();

        var json = BuildJson();

        AppendLine(
            $"[assembly: PragmaticMetadata(MetadataCategory.Endpoints, \"{MetadataSchemaVersions.Endpoints}\", \"\"\"");
        AppendLine(json);
        AppendLine("\"\"\")]");
    }

    /// <summary>
    ///     The metadata document this template writes. Public because a host that declares its own
    ///     endpoints has to hand the same document to Composition directly — the attribute above is
    ///     emitted into that same compilation and can never be read back off it.
    /// </summary>
    public string BuildJson()
    {
        var builder = new MetadataJsonBuilder(_indent);

        builder.StartObject();
        builder.Property("generator", "Pragmatic.Endpoints.SourceGenerator");

        // No library-side registration method — host generates route mapping directly from enriched metadata
        builder.PropertyNull("registrationMethod");

        builder.Property("data");
        builder.StartObject();

        // HasAspVersioning flag
        var hasAspVersioning = _endpoints.Any(e => e.HasAspVersioning);
        builder.Property("hasAspVersioning", hasAspVersioning);

        // Endpoints
        builder.Property("endpointsCount", _endpoints.Length);
        builder.Property("endpoints");
        builder.StartArray();

        foreach (var endpoint in _endpoints.OrderBy(e => e.FullTypeName))
        {
            builder.StartObject();

            // Ensure global:: prefix
            var typeName = endpoint.FullTypeName.StartsWith("global::", StringComparison.Ordinal)
                ? endpoint.FullTypeName
                : $"global::{endpoint.FullTypeName}";

            builder.Property("type", typeName);

            // ⚠️ Verb and route travel so the HOST can see what a single compilation cannot: two
            // operations at the same address. Two mutations with the same payload are two different
            // commands — that check was built and withdrawn — but two routes that are the same route
            // are a conflict a caller cannot resolve, whichever library each came from.
            builder.Property("verb", endpoint.HttpMethod);
            builder.Property("route", endpoint.Route);

            // A permission the author did not declare and cannot remove. Only the host can tell whether
            // anybody will ever hold it, so the fact travels rather than being judged here.
            if (endpoint.DerivedPermission is { Length: > 0 } derived)
                builder.Property("derivedPermission", derived);

            // An inline [RateLimit(Requests, Window)] registers its policy in this assembly's own
            // AddPragmaticEndpoints. The host does not call that — it builds its own registration —
            // so without carrying the numbers here the route requires a policy nobody created and
            // every request to it fails. Named policies are runtime configuration and stay out.
            if (endpoint.RateLimit is { Policy: null or "", Requests: > 0, Window: not null } inlineLimit)
            {
                builder.Property("rateLimitRequests", inlineLimit.Requests);
                builder.Property("rateLimitWindow", inlineLimit.Window);
            }

            // The processor types the generated handler resolves from the request services. They
            // travel for the same reason the rate limit numbers do: this assembly's own
            // AddPragmaticEndpoints registers them and the host does not call it, so without them
            // every request to a route carrying a processor answers 500.
            var processors = ProcessorRegistrationHelper.Collect(new[] { endpoint });
            if (processors.Count > 0)
                builder.PropertyArray("processors", processors);

            if (endpoint.Group is not null)
            {
                var groupType = endpoint.Group.TypeName.StartsWith("global::", StringComparison.Ordinal)
                    ? endpoint.Group.TypeName
                    : $"global::{endpoint.Group.TypeName}";
                builder.Property("group", groupType);
            }
            else
            {
                builder.PropertyNull("group");
            }

            builder.EndObject();
        }

        builder.EndArray();

        // Groups (unique, with full resolved route prefix)
        var groups = CollectUniqueGroups();
        builder.Property("groupsCount", groups.Count);
        builder.Property("groups");
        builder.StartArray();

        foreach (var group in groups.OrderBy(g => g.TypeName))
        {
            builder.StartObject();

            var groupType = group.TypeName.StartsWith("global::", StringComparison.Ordinal)
                ? group.TypeName
                : $"global::{group.TypeName}";

            builder.Property("type", groupType);
            builder.Property("routePrefix", GetFullRoutePrefix(group));

            if (group.Parent is not null)
            {
                var parentType = group.Parent.TypeName.StartsWith("global::", StringComparison.Ordinal)
                    ? group.Parent.TypeName
                    : $"global::{group.Parent.TypeName}";
                builder.Property("parent", parentType);
            }
            else
            {
                builder.PropertyNull("parent");
            }

            builder.EndObject();
        }

        builder.EndArray();

        builder.EndObject();
        builder.EndObject();

        return builder.ToString();
    }

    /// <summary>
    ///     Collects unique groups from all endpoints.
    /// </summary>
    private List<EndpointGroupModel> CollectUniqueGroups()
    {
        var seen = new HashSet<string>();
        var groups = new List<EndpointGroupModel>();

        foreach (var endpoint in _endpoints)
        {
            if (endpoint.Group is null)
                continue;

            // Walk the group chain (including parents)
            var current = endpoint.Group;
            while (current is not null)
            {
                if (seen.Add(current.TypeName))
                    groups.Add(current);
                current = current.Parent;
            }
        }

        return groups;
    }

    /// <summary>
    ///     Resolves the full route prefix by walking the parent chain.
    /// </summary>
    internal static string GetFullRoutePrefix(EndpointGroupModel group)
    {
        var prefixes = new List<string>();
        var current = group;

        while (current is not null)
        {
            if (!string.IsNullOrEmpty(current.RoutePrefix))
                prefixes.Insert(0, current.RoutePrefix!.TrimStart('/'));
            current = current.Parent;
        }

        if (prefixes.Count == 0)
            return string.Empty;

        return "/" + string.Join("/", prefixes);
    }

}
