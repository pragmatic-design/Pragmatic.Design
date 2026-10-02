using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Identity.Models;

namespace Pragmatic.SourceGenerator.Features.Identity.Transforms;

/// <summary>
///     Transforms <c>IRole</c> implementations into SG models. A permission is not a type: it is declared on
///     the assembly (<see cref="DeclaredPermissionTransform" />).
/// </summary>
/// <remarks>
///     A role is turned into a model even when its <c>Name</c> cannot be resolved: the model then carries an
///     empty name and <see cref="IdentityFeature" /> reports it (PRAG1003) before dropping it. Returning null
///     here instead is what made that diagnostic unreachable — the type disappeared before anything could
///     complain about it.
/// </remarks>
internal static class PermissionTransform
{
    public static RoleModel? TransformRole(
        GeneratorSyntaxContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (context.Node is not TypeDeclarationSyntax typeDecl)
            return null;

        if (context.SemanticModel.GetDeclaredSymbol(typeDecl, ct) is not INamedTypeSymbol symbol)
            return null;

        if (!ImplementsInterface(symbol, "IRole", "Pragmatic.Authorization"))
            return null;

        var defaultPermissions =
            GetStaticListPropertyValues(symbol, "DefaultPermissions", context.SemanticModel);

        return new RoleModel
        {
            Namespace = symbol.ContainingNamespace.IsGlobalNamespace
                ? "" : symbol.ContainingNamespace.ToDisplayString(),
            TypeName = symbol.Name,
            Accessibility = symbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            TypeKind = DescribeTypeKind(symbol),
            Name = GetStaticPropertyValue(symbol, "Name", context.SemanticModel) ?? "",
            Description = GetStaticPropertyValue(symbol, "Description", context.SemanticModel),
            DefaultPermissions = defaultPermissions.Resolved,
            UnresolvedDefaultPermissions = defaultPermissions.Unresolved,
            SpreadRoles = defaultPermissions.SpreadRoles,
            UnreadableSpreads = defaultPermissions.UnreadableSpreads,
            UnreadableReferences = defaultPermissions.UnreadableReferences,
            SourceTypeFqn = symbol.ToDisplayString()
        };
    }

    private static string DescribeTypeKind(INamedTypeSymbol symbol) => symbol switch
    {
        { IsRecord: true, TypeKind: Microsoft.CodeAnalysis.TypeKind.Struct } => "record struct",
        { IsRecord: true } => "record",
        { TypeKind: Microsoft.CodeAnalysis.TypeKind.Struct } => "struct",
        _ => "class"
    };

    private static bool ImplementsInterface(INamedTypeSymbol symbol, string interfaceName, string ns)
    {
        foreach (var iface in symbol.AllInterfaces)
        {
            if (iface.Name == interfaceName &&
                iface.ContainingNamespace.ToDisplayString() == ns)
                return true;
        }
        return false;
    }

    /// <summary>
    ///     Reads a static string property's compile-time value.
    /// </summary>
    /// <remarks>
    ///     Asks the semantic model for the constant value rather than pattern-matching the syntax for a
    ///     literal. <c>Name =&gt; PermissionNames.Read</c> is exactly as determinate as
    ///     <c>Name =&gt; "billing.read"</c>, and matching only the literal shape dropped it in silence.
    ///     Anything genuinely not constant (an interpolation, a method call) yields null and is reported
    ///     by the caller.
    /// </remarks>
    private static string? GetStaticPropertyValue(
        INamedTypeSymbol symbol, string propertyName, SemanticModel contextModel)
    {
        foreach (var member in symbol.GetMembers())
        {
            if (member is not IPropertySymbol prop || prop.Name != propertyName || !prop.IsStatic)
                continue;

            foreach (var syntaxRef in prop.DeclaringSyntaxReferences)
            {
                if (syntaxRef.GetSyntax() is not PropertyDeclarationSyntax propSyntax)
                    continue;

                var expr = PermissionListReader.GetValueExpression(propSyntax);
                if (expr is null)
                    continue;

                if (PermissionListReader.ModelFor(contextModel, expr).GetConstantValue(expr) is { HasValue: true, Value: string value })
                    return value;
            }
        }

        return null;
    }

    private static RoleListValues GetStaticListPropertyValues(
            INamedTypeSymbol symbol, string propertyName, SemanticModel contextModel)
    {
        foreach (var member in symbol.GetMembers())
        {
            if (member is not IPropertySymbol prop || prop.Name != propertyName || !prop.IsStatic)
                continue;

            foreach (var syntaxRef in prop.DeclaringSyntaxReferences)
            {
                if (syntaxRef.GetSyntax() is not PropertyDeclarationSyntax propSyntax)
                    continue;

                var expression = PermissionListReader.GetValueExpression(propSyntax);
                var values = PermissionListReader.ListValuesOf(expression, contextModel, depth: 0);
                if (values is not null)
                    return values;

                if (expression is null)
                    continue;

                // The whole property is a spread of another [Role]'s list, whose values this run writes.
                if (PermissionListReader.DeclaredRoleSpread(expression, contextModel) is { } role)
                    return RoleListValues.Empty with { SpreadRoles = new EquatableArray<string>(ImmutableArray.Create(role)) };

                // A list this compilation cannot read — in another assembly, and unpublished. Saying
                // "nothing" here is a falsehood the runtime contradicts: the caller reports it (PRAG1015).
                return RoleListValues.Unreadable(expression.ToString());
            }
        }

        return RoleListValues.Empty;
    }
}
