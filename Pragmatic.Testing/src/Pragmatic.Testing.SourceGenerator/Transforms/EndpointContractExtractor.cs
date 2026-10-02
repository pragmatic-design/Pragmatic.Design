using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.Testing.SourceGenerator.Models;

namespace Pragmatic.Testing.SourceGenerator.Transforms;

/// <summary>
///     Extracts an <see cref="EndpointContractModel"/> from an endpoint type by reading its
///     <c>[Endpoint(verb, route, Group)]</c> and <c>[RequirePermission]</c> — the verb, route and permission a
///     contract test needs (#7). The full route is the group's resolved prefix plus the endpoint's own route.
/// </summary>
internal static class EndpointContractExtractor
{
    private const string EndpointAttributeName = "EndpointAttribute";
    private const string RequirePermissionAttributeName = "RequirePermissionAttribute";
    private const string RequireAnyPermissionAttributeName = "RequireAnyPermissionAttribute";

    /// <summary>The generic <c>[Query&lt;TEntity, TResult&gt;]</c>, whose simple name carries the arity.</summary>
    private const string QueryAttributeName = "QueryAttribute";

    public static EndpointContractModel? Extract(INamedTypeSymbol endpointType, string? groupRoutePrefix)
    {
        var endpointAttr = endpointType.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == EndpointAttributeName);
        if (endpointAttr is null || endpointAttr.ConstructorArguments.Length < 2)
            return null;

        var verb = VerbName(endpointAttr.ConstructorArguments[0]);
        var route = endpointAttr.ConstructorArguments[1].Value as string ?? "";

        return new EndpointContractModel
        {
            Boundary = DeriveBoundary(endpointType),
            ActionName = StripSuffix(endpointType.Name, "Endpoint"),
            HttpMethod = verb,
            Route = CombineRoute(groupRoutePrefix, route),
            Permission = ReadPermission(endpointType),
            AnswersWithACollection = AnswersWithACollection(endpointType)
        };
    }

    /// <summary>
    ///     Whether the operation is a declared read that answers with many rows.
    /// </summary>
    /// <remarks>
    ///     A <c>[Query&lt;,&gt;]</c> answers with one resource only when it says <c>Single = true</c>;
    ///     otherwise it is a list or a page, and a filter matching nothing is an empty answer rather than a
    ///     404. Anything that is not a query — an endpoint, an action — keeps the old reading, which is
    ///     right for it: <c>GET /api/invoices/{id}/pdf</c> of an unknown invoice is a 404.
    /// </remarks>
    private static bool AnswersWithACollection(INamedTypeSymbol endpointType)
    {
        var query = endpointType.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == QueryAttributeName);
        if (query is null)
            return false;

        var single = query.NamedArguments
            .FirstOrDefault(argument => argument.Key == "Single").Value.Value as bool?;

        return single is not true;
    }

    /// <summary>Resolves the enum member name (e.g. <c>Get</c>) from the verb constructor argument.</summary>
    private static string VerbName(TypedConstant arg)
    {
        if (arg.Type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
        {
            var member = enumType.GetMembers().OfType<IFieldSymbol>()
                .FirstOrDefault(f => f.HasConstantValue && Equals(f.ConstantValue, arg.Value));
            if (member is not null)
                return member.Name;
        }

        return arg.Value?.ToString() ?? "Get";
    }

    /// <summary>
    ///     The permission a caller has to hold for the route to answer — from <c>[RequirePermission]</c>
    ///     or, failing that, from <c>[RequireAnyPermission]</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ Only the first was read, so an endpoint declaring the OR form produced <b>no</b>
    ///         contract tests at all: moving one to <c>[RequireAnyPermission]</c> silently removed the
    ///         two generated cases that said a caller without the permission is rejected and a caller
    ///         with it is not. Nothing failed — the suite simply had two fewer tests, which is only
    ///         visible if somebody counts.
    ///     </para>
    ///     <para>
    ///         One member of the OR set is enough for the pair of claims the generated tests make, and
    ///         both stay true under it: a caller with none of them is rejected, and a caller with this
    ///         one is not. What is <em>not</em> generated is a case per alternative — that belongs to
    ///         the example that declares the OR, because only the author knows which second audience
    ///         the route exists for.
    ///     </para>
    /// </remarks>
    private static string? ReadPermission(INamedTypeSymbol endpointType)
    {
        var attributes = endpointType.GetAttributes();

        var attr = attributes.FirstOrDefault(a => a.AttributeClass?.Name == RequirePermissionAttributeName)
                   ?? attributes.FirstOrDefault(a => a.AttributeClass?.Name == RequireAnyPermissionAttributeName);
        if (attr is not { ConstructorArguments.Length: > 0 })
            return null;

        // Both are [Attr(params string[] permissions)] — the arg is an array; take the first. For the
        // AND form the rest would also be needed, and that endpoint shape does not exist in the
        // examples; for the OR form the first alone is what the generated pair asserts.
        var arg = attr.ConstructorArguments[0];
        if (arg.Kind == TypedConstantKind.Array)
            return arg.Values.Length > 0 ? arg.Values[0].Value as string : null;

        return arg.Value as string;
    }

    private static string CombineRoute(string? prefix, string route)
    {
        var left = (prefix ?? "").TrimEnd('/');
        var right = route.TrimStart('/');
        if (string.IsNullOrEmpty(left))
            return "/" + right;
        return string.IsNullOrEmpty(right) ? left : $"{left}/{right}";
    }

    private static string DeriveBoundary(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace?.ToDisplayString() ?? "";
        var segments = ns.Split('.');
        return segments.Length >= 2 ? segments[1] : (segments.Length == 1 ? segments[0] : "App");
    }

    private static string StripSuffix(string name, string suffix) =>
        name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length
            ? name.Substring(0, name.Length - suffix.Length)
            : name;
}
