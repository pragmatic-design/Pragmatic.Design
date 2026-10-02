using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

internal sealed partial class EndpointRegistrationTemplate
{
    private void RenderMapPragmaticEndpoints()
    {
        XmlSummary("Maps all Pragmatic endpoints to the route builder.");
        XmlParam("endpoints", "The endpoint route builder.");
        XmlReturns("The endpoint route builder for chaining.");

        Method("MapPragmaticEndpoints", RenderMapPragmaticEndpointsBody,
            "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder",
            new List<MethodParameter>
            {
                new("Microsoft.AspNetCore.Routing.IEndpointRouteBuilder", "endpoints")
                {
                    IsExtension = true
                }
            },
            AccessModifier.Public,
            new MethodModifiers { IsStatic = true });
    }

    private void RenderMapPragmaticEndpointsBody()
    {
        // Apply global RoutePrefix from PragmaticEndpointsOptions — the registered instance, or the defaults
        EndpointRuntimeConfigRenderer.RenderOptionsResolution(AppendLine, "pragmaticOptions", "endpoints");
        AppendLine("var prefix = pragmaticOptions.RoutePrefix;");
        Comment("Always use MapGroup to get RouteGroupBuilder (IEndpointConventionBuilder) for auth/rate-limit");
        AppendLine("var root = endpoints.MapGroup(prefix);");
        AppendLine();

        // Global root options (shared with the Composition Host). A library cannot know
        // whether its host has authentication; [AnonymousHost] is read by the host's MapAllEndpoints.
        EndpointRuntimeConfigRenderer.RenderRootOptions(AppendLine, "pragmaticOptions", "root", anonymousHost: false);
        AppendLine();

        // Group endpoints by their group
        var groupedEndpoints = _endpoints
            .GroupBy(e => e.Group?.TypeName ?? string.Empty)
            .OrderBy(g => g.Key);

        foreach (var group in groupedEndpoints)
        {
            if (string.IsNullOrEmpty(group.Key))
            {
                // Ungrouped endpoints — use root (with prefix if configured)
                Comment("Ungrouped endpoints");
                foreach (var endpoint in group.OrderBy(e => e.Route))
                    AppendLine($"{endpoint.FullTypeName}.MapEndpoint(root);");
            }
            else
            {
                var firstEndpoint = group.First();
                var groupModel = firstEndpoint.Group!;

                // Calculate full route prefix including parent groups
                var fullPrefix = GetFullRoutePrefix(groupModel);

                Comment($"Group: {groupModel.TypeName}");

                // Every group gets its own route builder, prefix or not: MapGroup("") is route-neutral
                // and still carries the group's conventions. Mapping an empty-prefix group straight onto
                // root would drop its ConfigureGroup options without a word — and "a group
                // with no prefix, just for shared config" is exactly why an author declares one.
                var groupVarName = GetGroupVariableName(groupModel);
                AppendLine($"var {groupVarName} = root.MapGroup(\"{fullPrefix}\");");

                // Propagate group API version to all endpoints in the group
                if (!string.IsNullOrEmpty(groupModel.Version))
                    AppendLine($"{groupVarName}.WithMetadata(new Microsoft.AspNetCore.Mvc.ApiVersionAttribute(\"{groupModel.Version}\"));");

                // Apply programmatic ConfigureGroup() options at runtime (shared with the
                // Composition Host).
                var groupNameKey = groupModel.TypeName.Split('.').Last();
                // Strip "Group" suffix for key lookup (OrdersGroup → Orders)
                if (groupNameKey.EndsWith("Group") && groupNameKey.Length > 5)
                    groupNameKey = groupNameKey.Substring(0, groupNameKey.Length - 5);
                EndpointRuntimeConfigRenderer.RenderGroupOptions(
                    AppendLine, IncreaseIndent, DecreaseIndent,
                    "pragmaticOptions", groupVarName, groupNameKey);

                AppendLine();

                // Map endpoints in the group
                foreach (var endpoint in group.OrderBy(e => e.Route))
                    AppendLine($"{endpoint.FullTypeName}.MapEndpoint({groupVarName});");
            }

            AppendLine();
        }

        AppendLine("return endpoints;");
    }

    private static string GetFullRoutePrefix(EndpointGroupModel group)
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

    private static string GetGroupVariableName(EndpointGroupModel group)
    {
        var name = group.TypeName.Split('.').Last();
        if (name.Length > 5 && name.EndsWith("Group"))
            name = name.Substring(0, name.Length - 5);

        // Convert to camelCase (guard for single-char or empty names)
        return name.Length > 0
            ? char.ToLowerInvariant(name[0]) + (name.Length > 1 ? name.Substring(1) : "") + "Group"
            : "group";
    }
}
