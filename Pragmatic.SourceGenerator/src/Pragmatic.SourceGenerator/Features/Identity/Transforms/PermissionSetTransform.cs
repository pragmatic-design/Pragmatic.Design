using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Identity.Models;

namespace Pragmatic.SourceGenerator.Features.Identity.Transforms;

/// <summary>
///     Reads a <c>[PermissionSet]</c> list so this assembly can state what it holds.
/// </summary>
/// <remarks>
///     A role in another assembly reads such a list through its <c>DefaultPermissions</c>, and there the
///     list is metadata with no initializer to follow. The values are published here, on this assembly, and
///     read there from the statement.
/// </remarks>
internal static class PermissionSetTransform
{
    public const string PermissionSetAttribute = "Pragmatic.Authorization.PermissionSetAttribute";

    public static PermissionSetModel? Transform(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var values = context.TargetNode switch
        {
            VariableDeclaratorSyntax { Initializer.Value: { } initializer } =>
                PermissionListReader.ListValuesOf(initializer, context.SemanticModel, depth: 0),
            PropertyDeclarationSyntax property =>
                PermissionListReader.ListValuesOf(
                    PermissionListReader.GetValueExpression(property), context.SemanticModel, depth: 0),
            _ => null
        };

        // Marked and written in a shape that is not a list: publishing an empty list would answer
        // "grants nothing" with the generator's authority, and dropping it in silence would leave the
        // author with an attribute that does nothing. The feature reports it (PRAG1016).
        if (values is null)
            return new PermissionSetModel
            {
                MemberFqn = context.TargetSymbol.ToDisplayString(),
                CannotBeRead = true,
                Location = LocationInfo.From(context.TargetSymbol.Locations.FirstOrDefault())
            };

        // ⚠️ A list of this module's own permission constants folds to nothing here — the constants are
        // written by this same run. The paths travel and the feature resolves them against the
        // catalogue; deciding "unreadable" on Resolved alone made the attribute unusable on exactly the
        // list a module writes.
        return new PermissionSetModel
        {
            MemberFqn = context.TargetSymbol.ToDisplayString(),
            Values = values.Resolved,
            UnresolvedPaths = values.Unresolved,
            Location = LocationInfo.From(context.TargetSymbol.Locations.FirstOrDefault())
        };
    }
}
