using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Transforms;
using Pragmatic.SourceGenerator.Features.Identity.Models;

namespace Pragmatic.SourceGenerator.Features.Identity.Transforms;

/// <summary>
///     Reads <c>[Role]</c>, <c>[IncludesRole&lt;T&gt;]</c> and <c>[Grants]</c> on a class into a
///     <see cref="DeclaredRoleModel" />. Nothing is flattened here: an included role of this compilation may be
///     declared the same way, and a granted constant is often one this run generates — both are answered where
///     every role and the permission catalogue meet (<see cref="DeclaredRoleResolver" />).
/// </summary>
internal static class DeclaredRoleTransform
{
    public const string RoleAttribute = "Pragmatic.Authorization.RoleAttribute";
    private const string GrantsAttribute = "Pragmatic.Authorization.GrantsAttribute";
    private const string IncludesRoleAttribute = "IncludesRoleAttribute";
    private const string AuthorizationNamespace = "Pragmatic.Authorization";

    /// <summary>How deep an include chain of referenced assemblies is followed — it cannot cycle, but a guard costs nothing.</summary>
    private const int MaxReferenceDepth = 8;

    /// <remarks>
    ///     Total: every decorated class becomes a model, and a class that cannot take the members is reported by
    ///     the resolver (PRAG1006) rather than dropped here. The predicate is <c>IsClass</c>, so the target is a
    ///     type.
    /// </remarks>
    public static DeclaredRoleModel Transform(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var role = context.Attributes[0];
        var arguments = role.ConstructorArguments;
        var compilation = context.SemanticModel.Compilation;

        var grants = ImmutableArray.CreateBuilder<string>();
        var grantPaths = ImmutableArray.CreateBuilder<string>();
        var includes = ImmutableArray.CreateBuilder<string>();
        var fromReferences = ImmutableArray.CreateBuilder<string>();
        var unreadable = ImmutableArray.CreateBuilder<string>();

        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass is not { } attributeClass)
                continue;

            if (attributeClass.ToDisplayString() == GrantsAttribute)
            {
                var (resolved, unresolved) = ActionTransform.ExtractPermissionStrings(attribute, context.SemanticModel.Compilation);
                grants.AddRange(resolved);
                grantPaths.AddRange(unresolved);
                continue;
            }

            if (IsIncludesRole(attributeClass) && attributeClass.TypeArguments[0] is INamedTypeSymbol included)
            {
                // A role of this compilation is resolved with the others; a role elsewhere — a DLL, or in an
                // IDE a project of the same solution — is read from its attributes, the only part of it the
                // generator can see.
                if (IsInThisCompilation(included, compilation))
                    includes.Add(included.ToDisplayString());
                else
                    ReadReferencedRole(included, fromReferences, unreadable, depth: 0);
            }
        }

        return new DeclaredRoleModel
        {
            TypeName = symbol.Name,
            Namespace = symbol.ContainingNamespace.IsGlobalNamespace ? "" : symbol.ContainingNamespace.ToDisplayString(),
            FullName = symbol.ToDisplayString(),
            Name = arguments.Length > 0 ? arguments[0].Value as string ?? "" : "",
            Description = arguments.Length > 1 ? arguments[1].Value as string : null,
            CanBeGenerated = symbol.ContainingType is null && IsPartial(symbol, ct),
            Grants = grants.ToImmutable(),
            GrantPaths = grantPaths.ToImmutable(),
            Includes = includes.ToImmutable(),
            IncludedFromReferences = fromReferences.ToImmutable(),
            UnreadableIncludes = unreadable.ToImmutable(),
            Location = LocationInfo.From(role.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation())
        };
    }

    /// <summary>
    ///     A <c>[Role]</c> class of another assembly: its grants are constants that assembly's build resolved,
    ///     and its includes are read the same way. A hand-written <c>IRole</c> there has a property body nobody
    ///     here can read.
    /// </summary>
    private static void ReadReferencedRole(
        INamedTypeSymbol role, ImmutableArray<string>.Builder permissions, ImmutableArray<string>.Builder unreadable, int depth)
    {
        var attributes = role.GetAttributes();
        if (!attributes.Any(a => a.AttributeClass?.ToDisplayString() == RoleAttribute) || depth > MaxReferenceDepth)
        {
            unreadable.Add(role.ToDisplayString());
            return;
        }

        foreach (var attribute in attributes)
        {
            if (attribute.AttributeClass is not { } attributeClass)
                continue;

            if (attributeClass.ToDisplayString() == GrantsAttribute)
            {
                foreach (var argument in attribute.ConstructorArguments)
                {
                    var values = argument.Kind == TypedConstantKind.Array ? argument.Values : ImmutableArray.Create(argument);
                    foreach (var value in values)
                    {
                        if (value.Value is string permission)
                            permissions.Add(permission);
                    }
                }
            }
            else if (IsIncludesRole(attributeClass) && attributeClass.TypeArguments[0] is INamedTypeSymbol included)
            {
                ReadReferencedRole(included, permissions, unreadable, depth + 1);
            }
        }
    }

    private static bool IsIncludesRole(INamedTypeSymbol attributeClass)
        => attributeClass is { IsGenericType: true, Name: IncludesRoleAttribute, TypeArguments.Length: 1 }
           && attributeClass.ContainingNamespace?.ToDisplayString() == AuthorizationNamespace;

    private static bool IsInThisCompilation(INamedTypeSymbol type, Compilation compilation)
        => type.Locations.Any(l => l.IsInSource && l.SourceTree is { } tree && compilation.ContainsSyntaxTree(tree));

    private static bool IsPartial(INamedTypeSymbol symbol, CancellationToken ct)
        => symbol.DeclaringSyntaxReferences.Any(r =>
            r.GetSyntax(ct) is TypeDeclarationSyntax declaration && declaration.Modifiers.Any(SyntaxKind.PartialKeyword));
}
