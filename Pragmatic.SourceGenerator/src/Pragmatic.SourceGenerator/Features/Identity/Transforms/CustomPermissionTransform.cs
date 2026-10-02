using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Identity.Models;

namespace Pragmatic.SourceGenerator.Features.Identity.Transforms;

/// <summary>
///     Reads <c>[RequirePermission("x.y.z", Description = "...")]</c> (Mode 2) into the declared permissions —
///     the same path as <c>[assembly: Permission]</c>: a constant in the one <c>{Boundary}Permissions</c>
///     class, an entry in the permission registry.
/// </summary>
internal static class CustomPermissionTransform
{
    public static EquatableArray<DeclaredPermissionModel> Transform(
        GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var declared = ImmutableArray.CreateBuilder<DeclaredPermissionModel>();

        foreach (var attr in context.Attributes)
        {
            string? description = null;
            string? category = null;
            foreach (var namedArg in attr.NamedArguments)
            {
                if (namedArg is { Key: "Description", Value.Value: string desc })
                    description = desc;
                if (namedArg is { Key: "Category", Value.Value: string cat })
                    category = cat;
            }

            // Only Mode 2: without a Description the attribute requires a permission and declares none.
            if (description is null || attr.ConstructorArguments.Length == 0)
                continue;

            // An empty value is kept, as on the assembly attribute: the permissions class reports it (PRAG1004).
            var arg = attr.ConstructorArguments[0];
            var value = arg is { Kind: TypedConstantKind.Array, Values.Length: > 0 }
                ? arg.Values[0].Value as string
                : arg.Value as string;

            declared.Add(new DeclaredPermissionModel
            {
                Value = value ?? "",
                Description = description,
                Category = category,
                Source = $"[RequirePermission] on {context.TargetSymbol.Name}",
                Location = LocationInfo.From(attr.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation())
            });
        }

        return declared.ToImmutable();
    }
}
