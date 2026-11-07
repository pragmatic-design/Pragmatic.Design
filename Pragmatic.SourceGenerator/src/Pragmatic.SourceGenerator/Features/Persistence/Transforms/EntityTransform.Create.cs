using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

internal static partial class EntityTransform
{
    /// <summary>
    ///     The <c>Create</c> signatures of the entity's base entities (#540), by the rule the factory template applies to each: its own required
    ///     properties, and a <c>TimeProvider</c> when it is <c>[Auditable]</c>. An abstract base has no factory, and a base that is not an
    ///     <c>[Entity]</c> none either.
    /// </summary>
    internal static EquatableArray<string> InheritedCreateSignatures(INamedTypeSymbol typeSymbol)
    {
        var signatures = new List<string>();
        for (var current = typeSymbol.BaseType; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            if (current.IsAbstract || !HasEntityAttribute(current))
                continue;
            var auditable = HasAttribute(current, AuditableAttributeName);
            var properties = CollectProperties(current, CollectLogicKeys(current), auditable, HasAttribute(current, SoftDeleteAttributeName),
                HasAttribute(current, ConcurrencyAwareAttributeName), out _, out _);
            signatures.AddRange(CreateSignature.Of(properties.Where(p => p.IsRequiredForCreate).Select(p => p.TypeName).ToList(), auditable));
        }

        return signatures.Distinct().ToImmutableArray();
    }
}
