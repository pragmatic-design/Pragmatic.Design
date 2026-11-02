using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     The <c>[Autocomplete]</c> properties of an entity an engine persists: one read per property in the entity's repository
///     (<c>IAutocompleteReadRepository</c>), which the generated autocomplete endpoint calls by the property's name.
/// </summary>
/// <remarks>
///     Read off the entity, as <see cref="LoadIncludes" /> reads the loads: the endpoint is another feature's output, which this generator does
///     not see, and the read it asks for has to be compiled here. A property that is not a string is left out: the endpoint's own transform
///     refuses it, and no endpoint asks for it.
/// </remarks>
internal static class AutocompleteReads
{
    private const string Attribute = "Pragmatic.Endpoints.Attributes.AutocompleteAttribute";

    /// <summary>The names of <paramref name="entity" />'s string properties marked <c>[Autocomplete]</c> or <c>[Autocomplete&lt;TDto&gt;]</c>, in declaration order.</summary>
    public static EquatableArray<string> Of(INamedTypeSymbol entity)
    {
        var names = new List<string>();
        foreach (var property in entity.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.Type.SpecialType == SpecialType.System_String
                && property.GetAttributes().Any(a => a.AttributeClass?.OriginalDefinition.ToDisplayString() is { } name
                                                     && (name == Attribute || name.StartsWith(Attribute + "<", System.StringComparison.Ordinal))))
                names.Add(property.Name);
        }

        return names.ToEquatableArray();
    }
}
