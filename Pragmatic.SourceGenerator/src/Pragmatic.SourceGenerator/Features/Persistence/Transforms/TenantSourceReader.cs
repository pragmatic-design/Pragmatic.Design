using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Reads an entity's <c>[TenantSource]</c> and checks the members it names: the model the store is generated from, or why it cannot be
///     (<c>PRAG0642</c>). One reader for the entity's model and for the diagnostic, so the two cannot disagree on what is valid.
/// </summary>
internal static class TenantSourceReader
{
    private const string TenantStateType = "Pragmatic.MultiTenancy.TenantState";

    /// <summary>The entity declares <c>[TenantSource]</c>.</summary>
    public static AttributeData? AttributeOf(INamedTypeSymbol entity)
        => entity.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == AttributeNames.TenantSource);

    /// <summary>
    ///     The model of the entity's <c>[TenantSource]</c>, or null when it declares none or <paramref name="problem" /> says what is wrong.
    /// </summary>
    /// <param name="entity">The entity.</param>
    /// <param name="isAuditable">The entity is <c>[Auditable]</c>: its generated <c>CreatedAt</c> is the default registration instant.</param>
    /// <param name="problem">What is wrong with the declaration, for <c>PRAG0642</c>; null when nothing is.</param>
    public static TenantSourceModel? Read(INamedTypeSymbol entity, bool isAuditable, out string? problem)
    {
        problem = null;
        if (AttributeOf(entity) is not { } attribute)
            return null;

        string? Named(string key) => attribute.NamedArguments.FirstOrDefault(n => n.Key == key).Value.Value as string;

        var id = Named("Id");
        var name = Named("Name");
        var state = Named("State");
        var createdAt = Named("CreatedAt");

        problem = Check(entity, id, "Id", "string")
                  ?? Check(entity, name, "Name", "string")
                  ?? Check(entity, state, "State", TenantStateType)
                  ?? (createdAt is null ? null : Check(entity, createdAt, "CreatedAt", "System.DateTimeOffset"));
        if (problem is not null)
            return null;

        var nullable = createdAt is not null
                       && PropertyOf(entity, createdAt)!.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
        return new TenantSourceModel(id!, name!, state!, createdAt ?? (isAuditable ? "CreatedAt" : null), nullable);
    }

    /// <summary>Why <paramref name="member" /> cannot be what <paramref name="argument" /> names; null when it can.</summary>
    private static string? Check(INamedTypeSymbol entity, string? member, string argument, string type)
    {
        if (string.IsNullOrEmpty(member))
            return $"{argument} names no property";
        if (PropertyOf(entity, member!) is not { } property)
            return $"{argument} names '{member}', which is not a property of {entity.Name}";
        var declared = property.Type.WithNullableAnnotation(NullableAnnotation.None);
        var actual = declared is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable && type == "System.DateTimeOffset"
            ? nullable.TypeArguments[0]
            : declared;
        return actual.ToDisplayString() == type ? null : $"{argument} names '{member}', a {property.Type.ToDisplayString()}, where a {type} is needed";
    }

    private static IPropertySymbol? PropertyOf(INamedTypeSymbol entity, string name)
    {
        for (var type = entity; type is not null; type = type.BaseType)
        {
            if (type.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault(p => !p.IsStatic) is { } property)
                return property;
        }

        return null;
    }
}
