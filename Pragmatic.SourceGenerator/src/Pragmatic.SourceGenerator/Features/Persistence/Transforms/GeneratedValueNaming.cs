using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     The single source of truth for the name of the generated <c>[GeneratedValue]</c> default-value
///     generator.
/// </summary>
/// <remarks>
///     Two sides depend on this name and never meet: <c>GeneratedValueTransform</c> +
///     <c>GeneratedValueTemplate</c> <em>declare</em> the class, and
///     <c>ComputedDefaultEnricher</c> <em>references</em> it from the mutation invoker. When each side
///     built the name for itself, a rename on one side produced generated code pointing at a type that
///     does not exist — a compile error in the consumer's project, from the generator's own output.
/// </remarks>
internal static class GeneratedValueNaming
{
    private const string Suffix = "ValueGenerator";

    /// <summary>The generator's class name, e.g. <c>OrderOrderNumberValueGenerator</c>.</summary>
    public static string GeneratorClassName(string entityName, string propertyName)
        => NamingHelper.AppendSuffix(entityName + propertyName, Suffix);

    /// <summary>
    ///     The generator's fully-qualified name including the <c>global::</c> prefix. The generator is
    ///     emitted into the entity's own namespace.
    /// </summary>
    public static string GeneratorFullyQualifiedName(string entityNamespace, string entityName, string propertyName)
    {
        var className = GeneratorClassName(entityName, propertyName);
        return string.IsNullOrEmpty(entityNamespace)
            ? $"global::{className}"
            : $"global::{entityNamespace}.{className}";
    }
}
