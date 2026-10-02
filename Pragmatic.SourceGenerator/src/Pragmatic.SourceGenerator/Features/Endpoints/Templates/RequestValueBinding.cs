using System.Collections.Generic;
using System.Linq;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     Where a value the request carries — header, query, claim, cookie — reaches the operation: read
///     before it is built, set in its object initializer, or assigned after.
/// </summary>
/// <remarks>
///     <para>
///         An absent optional value must leave the property at what its declaration says. After
///         construction that is automatic — <c>if (x is not null) target.P = x;</c> never touches the
///         initializer — but only a <c>set</c> property can be assigned there. An <c>init</c> property,
///         the shape these classes are written in, can only be set in the initializer, so there the
///         declaration is spelled out: <c>P = x ?? &lt;declared default&gt;</c>. A <c>required</c> one
///         has to be there whatever it is.
///     </para>
///     <para>
///         ⚠️ Assigned after construction, an optional <c>init</c> header or query value would be
///         CS8852 in the generated file, and a claim or cookie CS9035 or CS8852. One place decides
///         it, for the five handler bodies that construct an operation. The query template keeps its
///         own header and query handling, which follows the same rule, and takes the claim and
///         cookie parts from the helpers directly. An <c>init</c> property whose initializer cannot be
///         reproduced gets no binding: PRAG0536 says so.
///     </para>
/// </remarks>
internal static class RequestValueBinding
{
    /// <summary>What has to run before the operation is built: the claim and cookie reads, and their refusals.</summary>
    public static IEnumerable<string> ReadLines(EndpointModel model)
        => ClaimBindingHelper.ReadLines(model).Concat(CookieBindingHelper.ReadLines(model));

    /// <summary>The initializer entries: optional values on <c>init</c> properties, and required claims and cookies.</summary>
    /// <param name="model">The endpoint.</param>
    /// <param name="includeQuery">Whether this handler binds query values at all.</param>
    public static IEnumerable<string> InitializerEntries(EndpointModel model, bool includeQuery = true)
    {
        foreach (var header in model.HeaderParameters.Where(p => !p.IsRequired && p.IsInitOnly && p.InitOnlyFallback is not null))
            yield return $"{header.PropertyName} = {ToCamelCase(header.PropertyName)} ?? {header.InitOnlyFallback}";

        if (includeQuery)
            foreach (var query in model.QueryParameters.Where(p => !p.IsRequired && p.IsInitOnly && p.InitOnlyFallback is not null))
                yield return $"{query.PropertyName} = {query.Name} ?? {query.InitOnlyFallback}";

        foreach (var entry in ClaimBindingHelper.InitializerEntries(model))
            yield return entry;

        foreach (var entry in CookieBindingHelper.InitializerEntries(model))
            yield return entry;
    }

    /// <summary>The assignments after construction: optional values on <c>set</c> properties.</summary>
    /// <param name="model">The endpoint.</param>
    /// <param name="target">The variable holding the constructed operation.</param>
    /// <param name="includeQuery">Whether this handler binds query values at all.</param>
    public static IEnumerable<string> PostConstructionLines(EndpointModel model, string target, bool includeQuery = true)
    {
        foreach (var header in model.HeaderParameters.Where(p => !p.IsRequired && !p.IsInitOnly))
        {
            var local = ToCamelCase(header.PropertyName);
            yield return $"if ({local} is not null) {target}.{header.PropertyName} = {local};";
        }

        if (includeQuery)
            foreach (var query in model.QueryParameters.Where(p => !p.IsRequired && !p.IsInitOnly))
            {
                // The local is nullable; a value-type property takes the value inside it.
                var value = query.IsValueType ? $"{query.Name}.Value" : query.Name;
                yield return $"if ({query.Name} is not null) {target}.{query.PropertyName} = {value};";
            }

        foreach (var line in ClaimBindingHelper.PostConstructionLines(model, target))
            yield return line;

        foreach (var line in CookieBindingHelper.PostConstructionLines(model, target))
            yield return line;
    }

    private static string ToCamelCase(string name)
        => string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);
}
