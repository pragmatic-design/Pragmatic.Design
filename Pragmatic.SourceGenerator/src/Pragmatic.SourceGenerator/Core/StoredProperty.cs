using System.Linq;
using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     Whether a property of an entity has a setter the engines may write through, wherever the entity was compiled.
/// </summary>
/// <remarks>
///     <para>
///         A type of a referenced assembly reaches the generator as metadata, and Roslyn imports only its public surface: a property
///         declared <c>{ get; private set; }</c> there has a <c>GetMethod</c> and no <c>SetMethod</c>, exactly like one declared
///         <c>=&gt; expression</c>. Read that way, another module's entity loses every column with a private setter and every
///         foreign key Pragmatic generates (<c>{Navigation}Id { get; private set; }</c>), which is what an entity read through
///         <c>[ReadAccess&lt;T&gt;]</c> on an engine ran into (#502).
///     </para>
///     <para>
///         What metadata keeps is the compiler's mark on an auto-property's accessors: its getter carries
///         <c>[CompilerGenerated]</c>, an expression-bodied one does not. The same test as the EF Core entity transform's
///         (<c>EntityTransform.Properties</c>). It cannot tell <c>{ get; private set; }</c> from <c>{ get; }</c>: a get-only
///         auto-property of a referenced entity is taken as stored, and its setter, which does not exist, is looked for at run time.
///     </para>
/// </remarks>
internal static class StoredProperty
{
    /// <summary>True when the property has a setter: a visible one, or, on a type compiled elsewhere, a non-public one its auto-property implies.</summary>
    public static bool HasSetter(IPropertySymbol property)
        => property.SetMethod is not null || (IsFromMetadata(property) && IsAutoProperty(property));

    /// <summary>True when the setter is public: false for a non-public one, and for one metadata does not show.</summary>
    public static bool HasPublicSetter(IPropertySymbol property) => property.SetMethod is { DeclaredAccessibility: Accessibility.Public };

    private static bool IsFromMetadata(IPropertySymbol property) => !property.Locations.Any(l => l.IsInSource);

    private static bool IsAutoProperty(IPropertySymbol property)
        => property.GetMethod is { } getter
           && getter.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.CompilerGeneratedAttribute");
}
