using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>
///     A result property fed by a <c>[Projectable]</c> member of the entity (#506), on the documents engine: the projection runs in .NET, so the
///     member's body (<see cref="ProjectableBody" />, the text EF Core's branch inlines) goes into it over the document. The selector names the
///     fields the document is read with, and the getter's are among them only when the body names them, hence the body and not the getter.
/// </summary>
internal static partial class DocQueries
{
    /// <summary>
    ///     The body of the projectable member <paramref name="property" /> names, over <c>e</c>; null when there is none, with a problem when its
    ///     body reads something that is not a field of the document.
    /// </summary>
    private static string? Computed(PragmaticQuery query, DocModel model, IPropertySymbol property, Compilation compilation, out string? problem)
    {
        problem = null;
        IPropertySymbol? member = null;
        // The entity of the compilation the body is read in, by name: a query a shape file declares (a [Resource]'s read) holds the shaped
        // compilation's symbol, and a symbol of one compilation is never equal to the same type's of another, so the body's members would not
        // be recognised as the entity's and would not be read from the document.
        var entity = compilation.GetTypeByMetadataName(query.Entity.ToDisplayString()) ?? query.Entity;
        for (var type = entity; type is not null && member is null; type = type.BaseType)
            member = type.GetMembers(property.Name).OfType<IPropertySymbol>().FirstOrDefault(p => !p.IsStatic && ProjectableBody.IsProjectable(p));
        if (member is null)
            return null;
        if (ProjectableBody.Of(member, compilation, "e") is not { } body)
        {
            problem = $"{query.Result.Name}.{property.Name} reads {query.Entity.Name}.{member.Name}, which is [Projectable] without an expression body";
            return null;
        }

        var read = SyntaxFactory.ParseExpression(body).DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>()
            .Where(a => a.Expression is IdentifierNameSyntax { Identifier.ValueText: "e" }).Select(a => a.Name.Identifier.ValueText)
            .FirstOrDefault(name => model.Field(name) is null);
        if (read is not null)
        {
            problem = $"{query.Result.Name}.{property.Name} reads {query.Entity.Name}.{member.Name}, whose body reads {read}, which is not a field of the document";
            return null;
        }

        return $"({body})";
    }
}
