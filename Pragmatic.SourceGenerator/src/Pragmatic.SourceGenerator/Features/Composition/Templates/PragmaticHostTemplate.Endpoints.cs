// Pragmatic.SourceGenerator - Composition - Pragmatic Host Template (Endpoints)

using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Composition.Models;
using Pragmatic.SourceGenerator.Features.Endpoints.Templates;

namespace Pragmatic.SourceGenerator.Features.Composition.Templates;

/// <summary>
///     Endpoint registration and mapping: RegisterAllEndpoints, MapAllEndpoints, and group variable helpers.
/// </summary>
internal sealed partial class PragmaticHostTemplate
{
    private void RenderRegisterAllEndpointsMethod()
    {
        XmlSummary("Registers all endpoint services from discovered assemblies.");
        XmlParam("services", "The service collection.");
        XmlReturns("The service collection for chaining.");

        var parameters = new List<MethodParameter>
        {
            new("this IServiceCollection", "services")
        };

        // Service-registration parity with library-mode AddPragmaticEndpoints.
        AddUsing("Microsoft.AspNetCore.RateLimiting");

        Method("RegisterAllEndpoints", () =>
        {
            if (!HasEndpoints)
            {
                Comment("No endpoint assemblies discovered");
                AppendLine("return services;");
                return;
            }

            // Resolve PragmaticEndpointsOptions (registered as a singleton before this runs)
            // by reading the descriptor directly — same approach as library AddPragmaticEndpoints.
            Comment("Read PragmaticEndpointsOptions from the registered descriptor (no BuildServiceProvider).");
            AppendLine("var __optsDescriptor = services.LastOrDefault(d => d.ServiceType == typeof(global::Pragmatic.Endpoints.Configuration.PragmaticEndpointsOptions) && d.Lifetime == global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton);");
            AppendLine("var __opts = __optsDescriptor?.ImplementationInstance as global::Pragmatic.Endpoints.Configuration.PragmaticEndpointsOptions;");
            AppendLine();

            EndpointRuntimeConfigRenderer.RenderApiDescriptions(AppendLine, "services");
            AppendLine();

            // Host-mode services (camelCase JSON / compression / problem-details) — shared with library.
            EndpointRuntimeConfigRenderer.RenderHostModeServices(
                AppendLine, Comment, IncreaseIndent, DecreaseIndent, "services", "__opts");
            AppendLine();

            if (_model.HasAspVersioning)
            {
                Comment("API versioning — auto-registered because at least one assembly has versioned endpoints.");
                Comment("AddApiVersioning() is idempotent; safe to call from multiple assemblies.");
                AppendLine("services.AddApiVersioning();");
                AppendLine();
            }

            // Rate limiter — always registered so runtime DefaultRateLimitPolicy/group/named policies
            // resolve, plus the inline [RateLimit] policies the endpoint assemblies
            // declared. They do not live in each assembly's own AddPragmaticEndpoints, which the host
            // does not call: the route would require a policy nobody had created, and every request to
            // it would fail with "no such policy exists". They travel in the assembly metadata for
            // exactly this.
            Comment("Rate limiting — inline [RateLimit(Requests, Window)] + named ConfigureRateLimiter() policies.");
            AppendLine("var rejectionCode = __opts?.RateLimitRejectionStatusCode ?? 429;");
            AppendLine("services.AddRateLimiter(rateLimiterOptions =>");
            AppendLine("{");
            IncreaseIndent();
            AppendLine("rateLimiterOptions.RejectionStatusCode = rejectionCode;");

            var inlineLimits = _model.DiscoveredEndpoints
                .Where(e => e.RateLimitRequests is > 0 && e.RateLimitWindow is not null)
                .OrderBy(e => e.EndpointType, StringComparer.Ordinal)
                .ToList();

            if (inlineLimits.Count > 0)
            {
                AppendLine();
                Comment("Inline policies — from [RateLimit(Requests, Window)] on the endpoint.");
                foreach (var endpoint in inlineLimits)
                {
                    var policyName = RateLimitPolicyName(endpoint.EndpointType);
                    // Per caller, like the library path: a shared bucket makes one tenant able to
                    // spend everyone else's permits.
                    var literal = StringHelper.CSharpLiteral(policyName);
                    AppendLine($"rateLimiterOptions.AddPolicy(\"{literal}\", httpContext =>");
                    AppendLine("    global::System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(");
                    AppendLine($"        global::Pragmatic.Endpoints.RateLimiting.RateLimitPartitionKey.For(httpContext, \"{literal}\"),");
                    AppendLine($"        _ => new global::System.Threading.RateLimiting.FixedWindowRateLimiterOptions {{ PermitLimit = {endpoint.RateLimitRequests}, Window = {WindowToTimeSpan(endpoint.RateLimitWindow!)} }}));");
                }
            }

            AppendLine();
            EndpointRuntimeConfigRenderer.RenderNamedRateLimiterPolicies(
                AppendLine, Comment, IncreaseIndent, DecreaseIndent, "rateLimiterOptions", "__opts");
            DecreaseIndent();
            AppendLine("});");
            AppendLine();

            // Processors travel in the assembly metadata for the same reason the inline rate limits
            // do: each endpoint assembly registers them in its own AddPragmaticEndpoints, which the
            // host does not call, so every request to a route carrying one answered 500 with "No
            // service for type '…' has been registered".
            ProcessorRegistrationHelper.Render(
                _model.DiscoveredEndpoints
                    .SelectMany(e => e.Processors.AsImmutableArray())
                    .Distinct()
                    .OrderBy(t => t, StringComparer.Ordinal)
                    .ToList(),
                "services",
                AppendLine,
                Comment);

            Comment("DomainAction invokers are registered by RegisterAllDomainActions().");
            Comment("Raw endpoints are instantiated with new() and need no DI registration.");
            AppendLine();
            AppendLine("return services;");
        }, "IServiceCollection", parameters, AccessModifier.Internal, new MethodModifiers { IsStatic = true });
    }

    /// <summary>
    ///     The policy name, which must match the one the endpoint's own configuration requires.
    /// </summary>
    /// <remarks>
    ///     Kept identical to <c>RateLimitHelper.GetInlinePolicyName</c> in the Endpoints feature. The
    ///     two live in different features and cannot share a helper without a reference between them;
    ///     when they disagree the endpoint requires a policy nobody registered, which is the failure
    ///     this code exists to fix.
    /// </remarks>
    private static string RateLimitPolicyName(string endpointType)
    {
        var trimmed = endpointType.Replace("global::", "");
        var chars = trimmed.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (!char.IsLetterOrDigit(chars[i]))
                chars[i] = '_';

        return "__pragmatic_ratelimit_" + new string(chars);
    }

    /// <summary>Mirrors the window parsing of the library-mode registration ("30s", "1m", "1h", "1d").</summary>
    private static string WindowToTimeSpan(string window)
    {
        if (window.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            return $"System.TimeSpan.FromSeconds({window.TrimEnd('s', 'S')})";
        if (window.EndsWith("m", StringComparison.OrdinalIgnoreCase))
            return $"System.TimeSpan.FromMinutes({window.TrimEnd('m', 'M')})";
        if (window.EndsWith("h", StringComparison.OrdinalIgnoreCase))
            return $"System.TimeSpan.FromHours({window.TrimEnd('h', 'H')})";
        if (window.EndsWith("d", StringComparison.OrdinalIgnoreCase))
            return $"System.TimeSpan.FromDays({window.TrimEnd('d', 'D')})";

        return $"System.TimeSpan.FromSeconds({window})";
    }

    private void RenderMapAllEndpointsMethod()
    {
        AddUsing("Microsoft.AspNetCore.Routing");

        // Parity with library-mode MapPragmaticEndpoints — apply route prefix, global root
        // options and per-group options. Needs Builder (RequireAuthorization/RequireRateLimiting/
        // CacheOutput/RequireCors), Http (TagsAttribute), DI (GetService<T>).
        AddUsing("Microsoft.AspNetCore.Builder");
        AddUsing("Microsoft.AspNetCore.Http");
        AddUsing("Microsoft.Extensions.DependencyInjection");

        XmlSummary("Maps all discovered endpoints to the application.");
        XmlParam("endpoints", "The endpoint route builder.");
        XmlReturns("The endpoint route builder for chaining.");

        var parameters = new List<MethodParameter>
        {
            new("this IEndpointRouteBuilder", "endpoints")
        };

        Method("MapAllEndpoints", () =>
        {
            if (!HasEndpoints)
            {
                Comment("No endpoint assemblies discovered");
                AppendLine("return endpoints;");
                return;
            }

            // Resolve options + create a root group carrying the global RoutePrefix, so the
            // Composition Host applies the same prefix/auth/rate-limit defaults as the library path.
            // Empty prefix => MapGroup("") is route-neutral. The remote-invoke dispatcher stays on
            // the raw `endpoints` (unprefixed infrastructure endpoint).
            EndpointRuntimeConfigRenderer.RenderOptionsResolution(AppendLine, "pragmaticOptions", "endpoints");
            AppendLine("var prefix = pragmaticOptions.RoutePrefix;");
            AppendLine("var root = endpoints.MapGroup(prefix);");
            EndpointRuntimeConfigRenderer.RenderRootOptions(AppendLine, "pragmaticOptions", "root",
                anonymousHost: _model.LocalModules.Any(m => m.IsAnonymousHost));
            AppendLine();

            // Build group variables (parent-first order)
            var groups = _model.DiscoveredEndpointGroups;
            if (groups is { IsDefaultOrEmpty: false, Length: > 0 })
            {
                // Emit parent groups before child groups
                var emitted = new HashSet<string>();
                var groupQueue = new Queue<DiscoveredEndpointGroupInfo>(
                    groups.OrderBy(g => g.RoutePrefix.Length));

                // maxIterations prevents an infinite loop when circular ParentGroupType refs exist.
                var maxIterations = groups.Length * groups.Length + 1;
                var iterations = 0;

                while (groupQueue.Count > 0)
                {
                    if (++iterations > maxIterations)
                        break; // Circular parent references detected — stop to avoid hang.

                    var group = groupQueue.Dequeue();
                    if (emitted.Contains(group.GroupType))
                        continue;

                    // If has parent that hasn't been emitted yet, re-queue
                    if (group.ParentGroupType is not null && !emitted.Contains(group.ParentGroupType))
                    {
                        groupQueue.Enqueue(group);
                        continue;
                    }

                    var varName = GetGroupVariableName(group.GroupType);
                    var target = group.ParentGroupType is not null
                        ? GetGroupVariableName(group.ParentGroupType)
                        : "root";

                    // For child groups, use only the child's own route segment (strip parent prefix)
                    var routeSegment = group.RoutePrefix;
                    if (group.ParentGroupType is not null)
                    {
                        var parentGroup = groups.FirstOrDefault(g => g.GroupType == group.ParentGroupType);
                        if (parentGroup is not null && !string.IsNullOrEmpty(parentGroup.RoutePrefix))
                        {
                            var parentPrefix = parentGroup.RoutePrefix;
                            if (routeSegment.StartsWith(parentPrefix, StringComparison.Ordinal))
                                routeSegment = routeSegment.Substring(parentPrefix.Length);
                        }
                    }

                    Comment($"Group: {GetSimpleName(group.GroupType)}");
                    AppendLine($"var {varName} = {target}.MapGroup(\"{routeSegment}\");");

                    // Apply programmatic ConfigureGroup() options at runtime, same
                    // as library-mode mapping. Key = group simple name minus "Group" suffix.
                    var groupKey = GetSimpleName(group.GroupType);
                    if (groupKey.Length > 5 && groupKey.EndsWith("Group", StringComparison.Ordinal))
                        groupKey = groupKey.Substring(0, groupKey.Length - 5);
                    EndpointRuntimeConfigRenderer.RenderGroupOptions(
                        AppendLine, IncreaseIndent, DecreaseIndent,
                        "pragmaticOptions", varName, groupKey);

                    AppendLine();
                    emitted.Add(group.GroupType);
                }
            }

            // Emit package endpoint groups (from [UsePackage<T>] route prefixes)
            var packageEndpoints = _model.DiscoveredEndpoints
                .Where(e => e.PackageRoutePrefix is not null)
                .GroupBy(e => e.PackageRoutePrefix!)
                .OrderBy(g => g.Key);

            var emittedPackageVars = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pkgGroup in packageEndpoints)
            {
                var pkgVarName = GetPackageGroupVariableName(pkgGroup.Key);
                if (emittedPackageVars.Add(pkgVarName))
                {
                    Comment($"Package: {pkgGroup.Key}");
                    AppendLine($"var {pkgVarName} = root.MapGroup(\"{pkgGroup.Key}\");");
                    AppendLine();
                }

                foreach (var ep in pkgGroup.OrderBy(e => e.EndpointType))
                    AppendLine($"{ep.EndpointType}.MapEndpoint({pkgVarName});");
                AppendLine();
            }

            // Exposed endpoints (from [ExposeEndpoint<T>] and [ExposeEndpoint<T, TGroup>])
            if (HasExposedEndpoints)
                RenderExposedEndpointMappings(emittedPackageVars);

            // Group non-package endpoints by their group for readability
            var nonPackageEndpoints = _model.DiscoveredEndpoints
                .Where(e => e.PackageRoutePrefix is null);

            var groupedEndpoints = nonPackageEndpoints
                .GroupBy(e => e.GroupType ?? string.Empty)
                .OrderBy(g => g.Key);

            foreach (var group in groupedEndpoints)
            {
                if (string.IsNullOrEmpty(group.Key))
                {
                    Comment("Ungrouped endpoints");
                    foreach (var ep in group.OrderBy(e => e.EndpointType))
                        AppendLine($"{ep.EndpointType}.MapEndpoint(root);");
                }
                else
                {
                    var varName = GetGroupVariableName(group.Key);
                    Comment($"Endpoints in {GetSimpleName(group.Key)}");
                    foreach (var ep in group.OrderBy(e => e.EndpointType))
                        AppendLine($"{ep.EndpointType}.MapEndpoint({varName});");
                }

                AppendLine();
            }

            // Map /_pragmatic/invoke dispatcher for remote boundary access
            // Only emitted when the PragmaticInvokeEndpoint class is generated (host has local actions)
            if (HasLocalActions)
            {
                Comment("Remote boundary invoke dispatcher");
                AppendLine($"{_model.RootNamespace}.PragmaticInvokeEndpoint.MapPragmaticInvoke(endpoints);");
                AppendLine();
            }

            AppendLine("return endpoints;");
        }, "IEndpointRouteBuilder", parameters, AccessModifier.Internal, new MethodModifiers { IsStatic = true });
    }

    /// <summary>
    ///     Derives a camelCase group variable name from a fully qualified group type.
    ///     E.g. "global::Showcase.Billing.Endpoints.InvoicesGroup" -> "invoicesGroup".
    /// </summary>
    private static string GetGroupVariableName(string groupTypeName)
    {
        var name = GetSimpleName(groupTypeName);
        if (name.Length > 5 && name.EndsWith("Group", StringComparison.Ordinal))
            name = name.Substring(0, name.Length - 5);

        return name.Length > 0
            ? char.ToLowerInvariant(name[0]) + (name.Length > 1 ? name.Substring(1) : "") + "Group"
            : "group";
    }

    /// <summary>
    ///     Derives a camelCase variable name from a package route prefix.
    ///     E.g. "identity/local" -> "identityLocalGroup".
    /// </summary>
    private static string GetPackageGroupVariableName(string routePrefix)
    {
        var parts = routePrefix.Split('/', '-', '.');
        var result = string.Empty;
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part)) continue;
            result += char.ToUpperInvariant(part[0]) + (part.Length > 1 ? part.Substring(1) : "");
        }

        if (result.Length == 0) return "packageGroup";
        return char.ToLowerInvariant(result[0]) + (result.Length > 1 ? result.Substring(1) : "") + "Group";
    }
}
