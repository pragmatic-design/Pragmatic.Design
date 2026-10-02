using System;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     Shared rendering of the runtime endpoint configuration — root-level options and
///     per-group <c>ConfigureGroup()</c> options — so the library-mode registration
///     (<see cref="EndpointRegistrationTemplate" />) and the Composition Host
///     (<c>PragmaticHostTemplate</c>) emit IDENTICAL behavior instead of diverging.
///     The methods take append delegates because the two consumers are unrelated
///     <c>CSharpTemplate</c> subclasses whose append API is <c>protected</c>.
/// </summary>
internal static class EndpointRuntimeConfigRenderer
{
    // ⚠️ No claim-type constant here: the requirement's handler asks IPermissionChecker, so nothing
    // generated names a claim type. A literal would ignore the configurable
    // IdentityOptions.PermissionClaimType, and a host renaming the claim would lose the gate in silence.

    /// <summary>
    ///     Resolves the options every root and group read goes through: the registered instance, or a
    ///     fresh one carrying the documented defaults when the host registered none.
    /// </summary>
    /// <remarks>
    ///     Reading the instance with <c>?.</c> and comparing with <c>== true</c> would make a host that
    ///     never configured the endpoints — every <c>PragmaticApp</c> host, since nothing generated
    ///     registers one — read <c>RequireAuthorizationByDefault</c> as <c>false</c>. The option is
    ///     documented <c>true</c>, "secure-by-default", and an anonymous write would answer 201.
    ///     Absence has to mean the defaults, not the opposite of one of them.
    /// </remarks>
    /// <param name="line">The template's AppendLine delegate.</param>
    /// <param name="optionsVar">Name of the variable to declare.</param>
    /// <param name="endpointsVar">Name of the in-scope IEndpointRouteBuilder.</param>
    public static void RenderOptionsResolution(Action<string> line, string optionsVar, string endpointsVar)
        => line($"var {optionsVar} = {endpointsVar}.ServiceProvider.GetService<global::Pragmatic.Endpoints.Configuration.PragmaticEndpointsOptions>() "
                + "?? new global::Pragmatic.Endpoints.Configuration.PragmaticEndpointsOptions();");

    /// <summary>
    ///     Applies the global root options (RequireAuthorizationByDefault with its
    ///     DefaultAuthorizationPolicy, DefaultRateLimitPolicy, EnableOpenApi) to the root convention
    ///     builder.
    /// </summary>
    /// <param name="line">The template's AppendLine delegate.</param>
    /// <param name="optionsVar">
    ///     Name of the in-scope PragmaticEndpointsOptions variable, declared by
    ///     <see cref="RenderOptionsResolution" /> and therefore never null.
    /// </param>
    /// <param name="rootVar">Name of the root RouteGroupBuilder variable.</param>
    /// <param name="anonymousHost">
    ///     True when the host declares <c>[AnonymousHost]</c>: it deliberately has no authentication, so
    ///     the authorization default is not emitted at all — known at compile time, not read at runtime.
    /// </param>
    public static void RenderRootOptions(Action<string> line, string optionsVar, string rootVar, bool anonymousHost)
    {
        if (!anonymousHost)
        {
            // The same shape a group's own AuthorizationPolicy takes: the named policy when there is
            // one, an authenticated user otherwise.
            line($"if ({optionsVar}.RequireAuthorizationByDefault)");
            line("{");
            line($"    if (!string.IsNullOrEmpty({optionsVar}.DefaultAuthorizationPolicy))");
            line($"        {rootVar}.RequireAuthorization({optionsVar}.DefaultAuthorizationPolicy);");
            line("    else");
            line($"        {rootVar}.RequireAuthorization();");
            line("}");
        }
        line($"if (!string.IsNullOrEmpty({optionsVar}.DefaultRateLimitPolicy))");
        line($"    {rootVar}.RequireRateLimiting({optionsVar}.DefaultRateLimitPolicy);");

        // On the root every generated endpoint hangs from, and nowhere else: the option removes
        // Pragmatic's operations from the runtime document, not the endpoints the application maps.
        line($"if (!{optionsVar}.EnableOpenApi)");
        line($"    global::Microsoft.AspNetCore.Http.OpenApiRouteHandlerBuilderExtensions.ExcludeFromDescription({rootVar});");
    }

    /// <summary>
    ///     Lets the API explorer, and so the runtime OpenAPI document, describe the generated endpoints.
    /// </summary>
    /// <remarks>
    ///     A generated endpoint is a <c>RequestDelegate</c>, which ASP.NET's own explorer does not
    ///     describe; this registers the provider that reads what the generator attached instead.
    /// </remarks>
    /// <param name="line">The template's AppendLine delegate.</param>
    /// <param name="servicesVar">Name of the in-scope IServiceCollection.</param>
    public static void RenderApiDescriptions(Action<string> line, string servicesVar)
        => line($"global::Pragmatic.Endpoints.ApiExplorer.PragmaticApiDescriptionExtensions.AddPragmaticApiDescriptions({servicesVar});");

    /// <summary>
    ///     Applies the programmatic per-group options (RequireAuthorization/policy, RateLimitPolicy,
    ///     RequiredPermissions, Tags, ResponseCacheDuration, CORS) resolved at runtime from
    ///     <c>PragmaticEndpointsOptions.Groups[groupKey]</c>.
    /// </summary>
    /// <param name="line">The template's AppendLine delegate.</param>
    /// <param name="increaseIndent">The template's IncreaseIndent delegate.</param>
    /// <param name="decreaseIndent">The template's DecreaseIndent delegate.</param>
    /// <param name="optionsVar">
    ///     Name of the in-scope PragmaticEndpointsOptions variable, declared by
    ///     <see cref="RenderOptionsResolution" /> and therefore never null.
    /// </param>
    /// <param name="groupVar">Name of the group's RouteGroupBuilder variable.</param>
    /// <param name="groupKey">Options dictionary key for this group (simple name minus "Group" suffix).</param>
    public static void RenderGroupOptions(
        Action<string> line, Action increaseIndent, Action decreaseIndent,
        string optionsVar, string groupVar, string groupKey)
    {
        line($"if ({optionsVar}.Groups.TryGetValue(\"{groupKey}\", out var __{groupVar}Opts))");
        line("{");
        increaseIndent();
        line($"if (__{groupVar}Opts.RequireAuthorization)");
        line("{");
        increaseIndent();
        line($"if (!string.IsNullOrEmpty(__{groupVar}Opts.AuthorizationPolicy))");
        line($"    {groupVar}.RequireAuthorization(__{groupVar}Opts.AuthorizationPolicy);");
        line("else");
        line($"    {groupVar}.RequireAuthorization();");
        decreaseIndent();
        line("}");
        line($"if (!string.IsNullOrEmpty(__{groupVar}Opts.RateLimitPolicy))");
        line($"    {groupVar}.RequireRateLimiting(__{groupVar}Opts.RateLimitPolicy);");

        // RequiredPermissions (AND) — the requirement, in the policy, exactly as an operation's
        // [RequirePermission] emits it. An opaque RequireAssertion comparing the token's claims by
        // hand would be wrong twice: nothing reading endpoint metadata could say what the group
        // required — so the 403 would come back without its requiredPermissions, the result handler
        // reading them off this very type — and comparing claims does not ask IPermissionChecker, so a
        // role-only token would be refused here while the same token passes on an operation-gated
        // route. The requirement's handler asks the checker, which is where roles and wildcards expand.
        //
        // The count is checked at run time because the list is the host's: the requirement's own
        // constructor rejects an empty one rather than granting, so building it unguarded would throw
        // for every group that configures nothing.
        line($"if (__{groupVar}Opts.RequiredPermissions.Count > 0)");
        line("{");
        increaseIndent();
        line($"{groupVar}.RequireAuthorization(policy => policy.AddRequirements(");
        line($"    new global::Pragmatic.Endpoints.Authorization.PragmaticPermissionRequirement(");
        line($"        __{groupVar}Opts.RequiredPermissions.ToArray(),");
        line("        global::Pragmatic.Endpoints.Authorization.PermissionMode.All)));");
        decreaseIndent();
        line("}");

        // Tags — attach as metadata (WithTags is RouteHandlerBuilder-specific).
        line($"if (__{groupVar}Opts.Tags.Count > 0)");
        line($"    {groupVar}.WithMetadata(new Microsoft.AspNetCore.Http.TagsAttribute(__{groupVar}Opts.Tags.ToArray()));");

        // ResponseCacheDuration — group-default output-cache expiry.
        line($"if (__{groupVar}Opts.ResponseCacheDuration is int __{groupVar}CacheDur)");
        line($"    {groupVar}.CacheOutput(c => c.Expire(System.TimeSpan.FromSeconds(__{groupVar}CacheDur)));");

        // CORS.
        line($"if (__{groupVar}Opts.EnableCors)");
        line("{");
        increaseIndent();
        line($"if (!string.IsNullOrEmpty(__{groupVar}Opts.CorsPolicy))");
        line($"    {groupVar}.RequireCors(__{groupVar}Opts.CorsPolicy);");
        line("else");
        line($"    {groupVar}.RequireCors();");
        decreaseIndent();
        line("}");
        decreaseIndent();
        line("}");
    }

    /// <summary>
    ///     Registers the host-mode ASP.NET Core services driven by PragmaticEndpointsOptions:
    ///     camelCase JSON, response compression, problem-details. Shared by library host-mode
    ///     AddPragmaticEndpoints and the Composition Host RegisterAllEndpoints.
    /// </summary>
    public static void RenderHostModeServices(
        Action<string> line, Action<string> comment, Action increaseIndent, Action decreaseIndent,
        string servicesVar, string optionsVar)
    {
        comment("JSON serialization — UseCamelCaseJson (default: true).");
        line($"if ({optionsVar}?.UseCamelCaseJson != false)");
        line("{");
        increaseIndent();
        line($"{servicesVar}.ConfigureHttpJsonOptions(jsonOpts =>");
        line("    jsonOpts.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);");
        decreaseIndent();
        line("}");
        line("");

        comment("Response compression — EnableResponseCompression (default: false).");
        line($"if ({optionsVar}?.EnableResponseCompression == true)");
        line($"    {servicesVar}.AddResponseCompression();");
        line("");

        comment("ProblemDetails — IncludeStackTraceInErrors (default: false).");
        line($"{servicesVar}.AddProblemDetails(problemOpts =>");
        line("{");
        increaseIndent();
        line($"if ({optionsVar}?.IncludeStackTraceInErrors == true)");
        line("{");
        increaseIndent();
        line("problemOpts.CustomizeProblemDetails = ctx =>");
        line("{");
        increaseIndent();
        line("if (ctx.Exception is not null)");
        line("    ctx.ProblemDetails.Extensions[\"stackTrace\"] = ctx.Exception.StackTrace;");
        decreaseIndent();
        line("};");
        decreaseIndent();
        line("}");
        decreaseIndent();
        line("});");
    }

    /// <summary>
    ///     Registers the named rate-limit policies configured at runtime via
    ///     PragmaticEndpointsOptions.ConfigureRateLimiter(). Shared by library AddPragmaticEndpoints
    ///     and the Composition Host RegisterAllEndpoints.
    ///     <paramref name="rateLimiterOptionsVar" /> is the RateLimiterOptions lambda parameter name;
    ///     <paramref name="optionsVar" /> is the in-scope PragmaticEndpointsOptions variable.
    /// </summary>
    public static void RenderNamedRateLimiterPolicies(
        Action<string> line, Action<string> comment, Action increaseIndent, Action decreaseIndent,
        string rateLimiterOptionsVar, string optionsVar)
    {
        comment("Named policies — from PragmaticEndpointsOptions.ConfigureRateLimiter().");
        comment("Per caller unless the configuration asks for one shared bucket (Global).");
        line($"if ({optionsVar}?.RateLimiters is {{ Count: > 0 }} rateLimiters)");
        line("{");
        increaseIndent();
        line("foreach (var (name, config) in rateLimiters)");
        line("{");
        increaseIndent();
        line("switch (config.Strategy)");
        line("{");
        increaseIndent();
        line("case global::Pragmatic.Endpoints.Configuration.RateLimiterStrategy.FixedWindow:");
        increaseIndent();
        line($"{rateLimiterOptionsVar}.AddPolicy(name, httpContext => config.Global");
        line($"    ? global::System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(name, _ => new global::System.Threading.RateLimiting.FixedWindowRateLimiterOptions {{ PermitLimit = config.PermitLimit, Window = config.Window, QueueLimit = config.QueueLimit }})");
        line($"    : global::System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(global::Pragmatic.Endpoints.RateLimiting.RateLimitPartitionKey.For(httpContext, name), _ => new global::System.Threading.RateLimiting.FixedWindowRateLimiterOptions {{ PermitLimit = config.PermitLimit, Window = config.Window, QueueLimit = config.QueueLimit }}));");
        line("break;");
        decreaseIndent();
        line("case global::Pragmatic.Endpoints.Configuration.RateLimiterStrategy.SlidingWindow:");
        increaseIndent();
        line($"{rateLimiterOptionsVar}.AddPolicy(name, httpContext => config.Global");
        line($"    ? global::System.Threading.RateLimiting.RateLimitPartition.GetSlidingWindowLimiter(name, _ => new global::System.Threading.RateLimiting.SlidingWindowRateLimiterOptions {{ PermitLimit = config.PermitLimit, Window = config.Window, SegmentsPerWindow = config.SegmentsPerWindow, QueueLimit = config.QueueLimit }})");
        line($"    : global::System.Threading.RateLimiting.RateLimitPartition.GetSlidingWindowLimiter(global::Pragmatic.Endpoints.RateLimiting.RateLimitPartitionKey.For(httpContext, name), _ => new global::System.Threading.RateLimiting.SlidingWindowRateLimiterOptions {{ PermitLimit = config.PermitLimit, Window = config.Window, SegmentsPerWindow = config.SegmentsPerWindow, QueueLimit = config.QueueLimit }}));");
        line("break;");
        decreaseIndent();
        line("case global::Pragmatic.Endpoints.Configuration.RateLimiterStrategy.TokenBucket:");
        increaseIndent();
        line($"{rateLimiterOptionsVar}.AddPolicy(name, httpContext => config.Global");
        line($"    ? global::System.Threading.RateLimiting.RateLimitPartition.GetTokenBucketLimiter(name, _ => new global::System.Threading.RateLimiting.TokenBucketRateLimiterOptions {{ TokenLimit = config.PermitLimit, ReplenishmentPeriod = config.Window, TokensPerPeriod = config.TokensPerPeriod > 0 ? config.TokensPerPeriod : config.PermitLimit, AutoReplenishment = config.AutoReplenishment, QueueLimit = config.QueueLimit }})");
        line($"    : global::System.Threading.RateLimiting.RateLimitPartition.GetTokenBucketLimiter(global::Pragmatic.Endpoints.RateLimiting.RateLimitPartitionKey.For(httpContext, name), _ => new global::System.Threading.RateLimiting.TokenBucketRateLimiterOptions {{ TokenLimit = config.PermitLimit, ReplenishmentPeriod = config.Window, TokensPerPeriod = config.TokensPerPeriod > 0 ? config.TokensPerPeriod : config.PermitLimit, AutoReplenishment = config.AutoReplenishment, QueueLimit = config.QueueLimit }}));");
        line("break;");
        decreaseIndent();
        line("case global::Pragmatic.Endpoints.Configuration.RateLimiterStrategy.Concurrency:");
        increaseIndent();
        line($"{rateLimiterOptionsVar}.AddPolicy(name, httpContext => config.Global");
        line($"    ? global::System.Threading.RateLimiting.RateLimitPartition.GetConcurrencyLimiter(name, _ => new global::System.Threading.RateLimiting.ConcurrencyLimiterOptions {{ PermitLimit = config.PermitLimit, QueueLimit = config.QueueLimit }})");
        line($"    : global::System.Threading.RateLimiting.RateLimitPartition.GetConcurrencyLimiter(global::Pragmatic.Endpoints.RateLimiting.RateLimitPartitionKey.For(httpContext, name), _ => new global::System.Threading.RateLimiting.ConcurrencyLimiterOptions {{ PermitLimit = config.PermitLimit, QueueLimit = config.QueueLimit }}));");
        line("break;");
        decreaseIndent();
        decreaseIndent();
        line("}");
        decreaseIndent();
        line("}");
        decreaseIndent();
        line("}");
    }
}

