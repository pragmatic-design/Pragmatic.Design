using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Identity.Models;

namespace Pragmatic.SourceGenerator.Features.Identity.Transforms;

/// <summary>
///     Reads <c>[assembly: Permission("value", "description", Category = "…")]</c> into the declared
///     permissions.
/// </summary>
internal static class DeclaredPermissionTransform
{
    /// <summary>The metadata name of the attribute.</summary>
    public const string AttributeName = "Pragmatic.Authorization.PermissionAttribute";

    public static EquatableArray<DeclaredPermissionModel> FromAssembly(
        GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var declared = ImmutableArray.CreateBuilder<DeclaredPermissionModel>();

        foreach (var attribute in context.Attributes)
        {
            // An empty value is kept, not skipped: the attribute compiles, so dropping it would leave a line
            // that does nothing. The permissions class reports it (PRAG1004).
            var arguments = attribute.ConstructorArguments;
            var value = arguments.Length > 0 ? arguments[0].Value as string : null;

            string? category = null;
            foreach (var named in attribute.NamedArguments)
            {
                if (named is { Key: "Category", Value.Value: string c })
                    category = c;
            }

            declared.Add(new DeclaredPermissionModel
            {
                Value = value ?? "",
                Description = arguments.Length > 1 ? arguments[1].Value as string : null,
                Category = category,
                Source = "[assembly: Permission]",
                Location = LocationInfo.From(attribute.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation())
            });
        }

        return declared.ToImmutable();
    }
}
