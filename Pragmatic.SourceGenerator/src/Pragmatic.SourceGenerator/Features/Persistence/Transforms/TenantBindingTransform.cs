using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>Reads the <c>[FromTenant]</c> properties of an operation.</summary>
internal static class TenantBindingTransform
{
    /// <summary>Every <c>[FromTenant]</c> property of the operation, with its problem if it has one.</summary>
    public static ImmutableArray<TenantBindingModel> Extract(INamedTypeSymbol operation)
    {
        var bindings = ImmutableArray.CreateBuilder<TenantBindingModel>();

        foreach (var property in operation.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.IsStatic || !InvokerBinding.IsFromTheTenant(property))
                continue;

            bindings.Add(new TenantBindingModel
            {
                PropertyName = property.Name,
                AcceptsNoTenant = property.NullableAnnotation == NullableAnnotation.Annotated,
                Problem = ProblemOf(property),
                Location = LocationInfo.From(property.Locations.FirstOrDefault())
            });
        }

        return bindings.ToImmutable();
    }

    private static string? ProblemOf(IPropertySymbol property)
    {
        if (property.Type.SpecialType != SpecialType.System_String)
            return $"it is '{property.Type.ToDisplayString()}', and the tenant is a string (ITenantContext.TenantId)";

        // The clock's rule, for the clock's reason: any wider setter is a value a caller writes and the
        // invoker then overwrites — and here the value is which tenant's data the operation reads.
        return property.SetMethod is { IsInitOnly: false, DeclaredAccessibility: Accessibility.Private }
            ? null
            : "its caller can set it; declare it '{ get; private set; }', so the generated invoker is the only one to write it";
    }
}
