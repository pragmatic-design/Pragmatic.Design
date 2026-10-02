using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     The DI registration for the processor types <c>[PreProcessor&lt;T&gt;]</c> and
///     <c>[PostProcessor&lt;T&gt;]</c> name.
/// </summary>
/// <remarks>
///     <para>
///         The generated handler resolves each processor with
///         <c>RequestServices.GetRequiredService&lt;TProcessor&gt;()</c>. Nothing else registers those
///         types — a processor carries no <c>[Service]</c> — so the generator that emitted the
///         resolution emits the registration too. Without it every request to a route carrying a
///         processor answered 500.
///     </para>
///     <para>
///         Two callers render the same lines: the assembly's own <c>AddPragmaticEndpoints</c>, and the
///         Composition host's <c>RegisterAllEndpoints</c>, which does not call it and builds its own
///         registration from the assembly metadata.
///     </para>
/// </remarks>
internal static class ProcessorRegistrationHelper
{
    /// <summary>
    ///     The distinct processor types declared across <paramref name="endpoints" /> that the
    ///     container can construct, in a stable order.
    /// </summary>
    /// <remarks>
    ///     A type PRAG0534 reports is left out: there is no registration for it that would resolve,
    ///     and emitting one would turn a build error into a request that fails at run time.
    /// </remarks>
    public static List<string> Collect(IEnumerable<EndpointModel> endpoints)
    {
        return endpoints
            .SelectMany(e => e.PreProcessors.AsImmutableArray().Concat(e.PostProcessors.AsImmutableArray()))
            .Where(p => p.NotConstructibleReason is null)
            .Select(p => p.TypeName)
            .Distinct()
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    ///     Renders one <c>TryAddScoped</c> per processor type.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Scoped</b>, because a processor is a per-request collaborator that may inject
    ///         anything the request can reach — a repository, a DbContext, the current user. Singleton
    ///         would throw on the first resolution for every processor with a scoped dependency, and
    ///         the handler already resolves from <c>HttpContext.RequestServices</c>, so scoped means
    ///         one instance per request either way.
    ///     </para>
    ///     <para>
    ///         <b>TryAdd</b>, so an application that registered the type itself keeps its own
    ///         registration — a different lifetime, or a factory supplying an argument the container
    ///         cannot resolve on its own. Same shape as the <c>[PresetProvider&lt;T&gt;]</c>
    ///         registrations.
    ///     </para>
    /// </remarks>
    public static void Render(
        List<string> processorTypes,
        string servicesExpression,
        Action<string> appendLine,
        Action<string> comment)
    {
        if (processorTypes.Count == 0)
            return;

        comment("Processors from [PreProcessor<T>]/[PostProcessor<T>] — the generated handler resolves");
        comment("them from the request services, so they are registered here. Scoped: a processor may");
        comment("inject anything the request can reach. TryAdd: a hand registration wins.");

        foreach (var processorType in processorTypes)
        {
            appendLine(
                "global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddScoped<" +
                $"{processorType}>({servicesExpression});");
        }

        appendLine(string.Empty);
    }
}
