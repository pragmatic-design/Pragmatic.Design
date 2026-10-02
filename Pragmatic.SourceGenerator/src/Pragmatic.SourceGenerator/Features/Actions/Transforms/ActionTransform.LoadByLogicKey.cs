using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

internal static partial class ActionTransform
{
    /// <summary>
    ///     A <c>[LoadEntity(By = …)]</c>: the key property holds the entity's logic key, read through the lookup the
    ///     generator writes for it — or why it cannot be.
    /// </summary>
    /// <remarks>
    ///     The key is read by <see cref="MutationLogicalKeyReader" />, which asks <c>EntityTransform.CollectLogicKeys</c>
    ///     — the same reading the unique index and the generated <c>By{Key}</c> / <c>GetBy{Key}Async</c> come from — so
    ///     the member checked here is the member the lookup filters by. One part only: a composite key has no single
    ///     value for one property to hold.
    /// </remarks>
    private static (LoadEntityModel? Model, LoadEntityDiagnosticInfo? Problem) ReadByLogicKey(
        LoadEntityModel common, ITypeSymbol entityType, IPropertySymbol keyProperty, string member,
        Compilation compilation)
    {
        LoadEntityDiagnosticInfo NotTheKey(string reason) => new()
        {
            EntityTypeName = entityType.Name,
            Kind = LoadEntityDiagnosticKind.ByNotTheLogicKey,
            Reason = reason
        };

        if (entityType is not INamedTypeSymbol entity)
            return (null, NotTheKey($"'{entityType.Name}' is not an entity"));

        var parts = MutationLogicalKeyReader.ReadTyped(entity, compilation);
        if (parts.IsEmpty)
            return (null, NotTheKey($"'{entity.Name}' declares no [LogicKey]"));
        if (parts.Length > 1)
            return (null, NotTheKey(
                $"'{entity.Name}'s [LogicKey] has {parts.Length} parts ({string.Join(", ", parts.Select(p => p.Part.Name))}), "
                + "and By loads by a single-part key"));

        var (part, keyType) = parts[0];
        if (part.Name != member)
            return (null, NotTheKey($"'{member}' is not the logic key of '{entity.Name}' — its logic key is '{part.Name}'"));
        if (keyType is null)
            return (null, NotTheKey(
                $"the logic key '{part.Name}' of '{entity.Name}' is a key a relation declared on another entity puts "
                + "there, and its type is known only to the relation graph"));

        if (!SymbolEqualityComparer.Default.Equals(keyProperty.Type, keyType))
            return (null, new LoadEntityDiagnosticInfo
            {
                EntityTypeName = entity.Name,
                Kind = LoadEntityDiagnosticKind.ByKeyTypeMismatch,
                IdPropertyName = keyProperty.Name,
                SuppliedKeyType = keyProperty.Type.ToDisplayString(),
                EntityKeyType = keyType.ToDisplayString(),
                LogicKeyMember = member
            });

        var ns = entity.ContainingNamespace is { IsGlobalNamespace: false } containing
            ? containing.ToDisplayString() + "."
            : "";

        return (common with
        {
            IdPropertyName = keyProperty.Name,
            IdTypeFullName = keyProperty.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            KeyTypeFullName = keyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            LogicKeyMember = member,
            LogicKeyLookupClass = $"global::{ns}{entity.Name}Specifications"
        }, null);
    }
}
