using System;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
/// Shared helpers for templates in the unified SG.
/// Avoids repeating the same static methods in every template class.
/// </summary>
internal static class TemplateHelpers
{
    /// <summary>
    ///     Fields the invoker base classes already declare, which a generated dependency field must not
    ///     take over.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>ActionInvokerBase</c> and <c>MutationInvoker</c> declare these as <c>protected</c>. An
    ///         action that injects an <c>ILogger&lt;T&gt;</c> — an ordinary thing to do — would make the
    ///         generated invoker declare <c>_logger</c> over the base's, which is CS0108: a warning in
    ///         code the author did not write, and a build failure wherever warnings are errors.
    ///     </para>
    /// </remarks>
    private static readonly string[] InvokerBaseFields =
        ["_logger", "_serviceProvider", "GlobalFilters", "ServiceProvider", "Compensation"];

    /// <summary>
    ///     The name the invoker gives a dependency field, which is its own business: the action keeps
    ///     the name its author chose, and only the generated holder is renamed on a collision.
    /// </summary>
    public static string InvokerFieldName(string fieldName)
        => Array.IndexOf(InvokerBaseFields, fieldName) >= 0 ? fieldName + "Dependency" : fieldName;

    public static AccessModifier ParseAccessibility(string accessibility) =>
        accessibility.ToLowerInvariant() switch
        {
            "public" => AccessModifier.Public,
            "internal" => AccessModifier.Internal,
            "protected" => AccessModifier.Protected,
            "private" => AccessModifier.Private,
            _ => AccessModifier.Public
        };

    public static string ToCamelCase(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);
}
