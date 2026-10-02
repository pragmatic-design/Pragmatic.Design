using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     Generates the MapPragmaticEndpoints extension method that registers all endpoints.
/// </summary>
internal sealed partial class EndpointRegistrationTemplate : CSharpTemplate
{
    private readonly ImmutableArray<EndpointModel> _endpoints;
    private readonly bool _isHostMode;

    public EndpointRegistrationTemplate(ImmutableArray<EndpointModel> endpoints, bool isHostMode = false)
    {
        _endpoints = endpoints;
        _isHostMode = isHostMode;
    }

    protected override string? GeneratorName => "Pragmatic.Endpoints";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForAssembly("Endpoints", "Registration"),
            ToSourceText());
    }

    protected override bool Validate()
    {
        return !_endpoints.IsEmpty;
    }

    public override void RenderFile()
    {
        AddUsing("Microsoft.AspNetCore.Builder");
        AddUsing("Microsoft.AspNetCore.Routing");
        AddUsing("Microsoft.Extensions.DependencyInjection");

        // The rate limiter registration is always emitted, so the limiter extensions
        // (AddFixedWindowLimiter etc.) namespace is always required.
        AddUsing("Microsoft.AspNetCore.RateLimiting");

        // Use the first endpoint's namespace or a reasonable default
        var extensionNamespace = DeriveExtensionNamespace();

        AppendNamespace(extensionNamespace);
        AppendLine();

        XmlSummary("Extension methods for registering Pragmatic endpoints.");

        Class("PragmaticEndpointsRegistrationExtensions", RenderClassBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderClassBody()
    {
        RenderMapPragmaticEndpoints();
        AppendLine();
        RenderAddPragmaticEndpoints();
    }

    private void RenderAddPragmaticEndpoints()
    {
        XmlSummary("Registers all Pragmatic endpoint services for dependency injection.");
        XmlParam("services", "The service collection.");
        XmlReturns("The service collection for chaining.");

        Method("AddPragmaticEndpoints", RenderAddPragmaticEndpointsBody,
            "Microsoft.Extensions.DependencyInjection.IServiceCollection",
            new List<MethodParameter>
            {
                new("Microsoft.Extensions.DependencyInjection.IServiceCollection", "services")
                {
                    IsExtension = true
                }
            },
            AccessModifier.Public,
            new MethodModifiers { IsStatic = true });
    }

    private void RenderAddPragmaticEndpointsBody()
    {
        Comment("Read PragmaticEndpointsOptions that was registered by a prior AddPragmaticEndpoints() call.");
        Comment("We avoid BuildServiceProvider() (which creates a second root container) by reading the descriptor directly.");
        AppendLine("var __optsDescriptor = services.LastOrDefault(d => d.ServiceType == typeof(global::Pragmatic.Endpoints.Configuration.PragmaticEndpointsOptions) && d.Lifetime == global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton);");
        AppendLine("var __opts = __optsDescriptor?.ImplementationInstance as global::Pragmatic.Endpoints.Configuration.PragmaticEndpointsOptions;");
        AppendLine();

        EndpointRuntimeConfigRenderer.RenderApiDescriptions(AppendLine, "services");
        AppendLine();

        // ASP.NET Core auto-wiring — only in Host mode (not in library/test projects).
        // Shared with the Composition Host RegisterAllEndpoints.
        if (_isHostMode)
        {
            EndpointRuntimeConfigRenderer.RenderHostModeServices(
                AppendLine, Comment, IncreaseIndent, DecreaseIndent, "services", "__opts");
            AppendLine();
        }

        // Auto-register API versioning if any endpoint in this assembly uses ExecuteV2+ convention
        // (detected by Asp.Versioning.ApiVersion presence in compilation — HasAspVersioning flag)
        if (_endpoints.Any(e => e.HasAspVersioning))
        {
            Comment("API versioning — auto-registered because this assembly has versioned endpoints.");
            Comment("AddApiVersioning() is idempotent; safe to call from multiple assemblies.");
            AppendLine("services.AddApiVersioning();");
            AppendLine();
        }

        // Rate limiting: one AddRateLimiter call covering inline + named policies.
        // Always register the rate limiter services. Inline/endpoint-level policies are
        // known at compile time, but a global DefaultRateLimitPolicy, a group RateLimitPolicy, or
        // named policies via ConfigureRateLimiter() are runtime options — mapping would
        // RequireRateLimiting a policy that was never registered if we gated on compile-time data.
        var inlineRateLimitEndpoints = _endpoints
            .Where(e => e.RateLimit is { Policy: null or "", Requests: > 0, Window: not null })
            .ToList();

        {
            Comment("Rate limiting — inline [RateLimit(Requests, Window)] + named ConfigureRateLimiter() policies.");
            Comment("Always registered so runtime DefaultRateLimitPolicy/group/named policies resolve.");
            AppendLine("var endpointOptions = __opts;");
            AppendLine("var rejectionCode = endpointOptions?.RateLimitRejectionStatusCode ?? 429;");
            AppendLine("services.AddRateLimiter(rateLimiterOptions =>");
            AppendLine("{");
            IncreaseIndent();
            AppendLine("rateLimiterOptions.RejectionStatusCode = rejectionCode;");

            // Inline policies from [RateLimit(Requests, Window)]
            if (inlineRateLimitEndpoints.Count > 0)
            {
                AppendLine();
                Comment("Inline policies — SG-generated from [RateLimit(Requests, Window)]");
                foreach (var endpoint in inlineRateLimitEndpoints)
                {
                    var policyName = RateLimitHelper.GetInlinePolicyName(endpoint);
                    // AddPolicy, not AddFixedWindowLimiter: the latter builds one bucket for the
                    // whole route, so "N per window" — which every reader takes for a per-caller
                    // quota — let one tenant in a retry loop refuse all the others.
                    var literal = StringHelper.CSharpLiteral(policyName);
                    AppendLine($"rateLimiterOptions.AddPolicy(\"{literal}\", httpContext =>");
                    AppendLine("    global::System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(");
                    AppendLine($"        global::Pragmatic.Endpoints.RateLimiting.RateLimitPartitionKey.For(httpContext, \"{literal}\"),");
                    AppendLine($"        _ => new global::System.Threading.RateLimiting.FixedWindowRateLimiterOptions {{ PermitLimit = {endpoint.RateLimit!.Requests}, Window = {ParseWindowToTimeSpan(endpoint.RateLimit.Window!)} }}));");
                }
            }

            // Named policies from PragmaticEndpointsOptions.ConfigureRateLimiter() (shared with the Composition Host).
            AppendLine();
            EndpointRuntimeConfigRenderer.RenderNamedRateLimiterPolicies(
                AppendLine, Comment, IncreaseIndent, DecreaseIndent, "rateLimiterOptions", "endpointOptions");

            DecreaseIndent();
            AppendLine("});");
            AppendLine();
        }

        ProcessorRegistrationHelper.Render(
            ProcessorRegistrationHelper.Collect(_endpoints), "services", AppendLine, Comment);

        Comment("DomainAction invokers are registered by the generated Add{Prefix}Actions() extension");
        Comment("(or by the Composition host). AddPragmaticActions() only wires the pipeline (filters,");
        Comment("call context, authorization registries) — it does NOT register invokers.");
        Comment("Raw endpoints are instantiated with new() and need no DI registration.");
        Comment("Standalone (no host): call BOTH AddPragmaticActions() and the generated AddActions()");
        Comment("before MapPragmaticEndpoints() so IDomainActionInvoker<T,R> resolves.");
        AppendLine();
        AppendLine("return services;");
    }

    /// <summary>
    ///     Derives the host namespace (up to .Api or similar) for the endpoints registration.
    ///     e.g., "Contoso.University.Api.Endpoints.Students" -> "Contoso.University.Api"
    /// </summary>
    private string DeriveHostNamespace()
    {
        var namespaces = _endpoints
            .Select(e => e.Namespace)
            .Where(ns => !string.IsNullOrEmpty(ns))
            .ToList();

        if (namespaces.Count == 0)
            return string.Empty;

        var firstNamespace = namespaces[0];
        var parts = firstNamespace.Split('.');

        // Find the "Api" or "Host" segment and include up to that
        var apiSuffixes = new[] { "Api", "Host", "Web", "Server" };
        for (var i = 0; i < parts.Length; i++)
            if (apiSuffixes.Any(suffix => parts[i].EndsWith(suffix)))
                return string.Join(".", parts.Take(i + 1));

        // Fallback: remove "Endpoints" and following segments
        var prefixParts = parts.TakeWhile(p => p != "Endpoints").ToArray();
        return prefixParts.Length > 0 ? string.Join(".", prefixParts) : parts[0];
    }

    private string DeriveExtensionNamespace()
    {
        // Use the host namespace (Api/Host level) for the extension class
        // This puts it at a sensible level for consumption
        var hostNs = DeriveHostNamespace();
        if (!string.IsNullOrEmpty(hostNs))
            return hostNs;

        // Fallback: longest common namespace prefix across the endpoints.
        var prefix = NamespacePrefixHelper.DerivePrefix(_endpoints.Select(e => e.Namespace));

        return string.IsNullOrEmpty(prefix)
            ? "Pragmatic.Endpoints.Generated"
            : prefix;
    }
}
