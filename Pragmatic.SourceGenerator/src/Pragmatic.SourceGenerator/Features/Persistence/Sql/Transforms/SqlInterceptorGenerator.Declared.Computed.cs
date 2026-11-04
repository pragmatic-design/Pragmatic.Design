using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

internal static partial class SqlInterceptorGenerator
{
    internal sealed partial class Projection
    {
        private const string ComputedSource = "__e";

        /// <summary>
        ///     A DTO property fed by a <c>[Projectable]</c> member of the entity (#506), by name or by a one-segment <c>[MapProperty]</c>: the
        ///     member's body (<see cref="ProjectableBody" />, the same text EF Core's branch inlines), with each column it reads selected and read
        ///     in its place, so the value is computed in .NET as the getter computes it, from the columns the statement read. Null when the
        ///     property names no projectable member; a problem when its body reads something that is not a column of the entity.
        /// </summary>
        /// <remarks>
        ///     Computed in .NET rather than translated: the body is C# (<c>(CheckOut - CheckIn).Days</c>), and a date difference has a
        ///     different meaning per dialect (a day boundary crossed, not 24 hours elapsed). The columns are the entity's, read once.
        /// </remarks>
        private static string? TryComputed(IPropertySymbol property, string? path, EntityModel entity, Compilation? compilation, List<string> columns, out string? problem)
        {
            problem = null;
            if (compilation is null || (path is not null && path.Contains('.')))
                return null;
            var name = path ?? property.Name;
            IPropertySymbol? member = null;
            for (var type = entity.Type; type is not null && member is null; type = type.BaseType)
                member = type.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault(p => !p.IsStatic && ProjectableBody.IsProjectable(p));
            if (member is null)
                return null;
            if (ProjectableBody.Of(member, compilation, ComputedSource) is not { } body)
            {
                problem = $"{entity.Type.Name}.{name} is [Projectable] without a body to read: give it an expression body";
                return null;
            }

            var read = new ColumnReads(entity, columns);
            var rewritten = read.Visit(SyntaxFactory.ParseExpression(body));
            if (read.Problem is { } what)
            {
                problem = $"{entity.Type.Name}.{name} computes {body.Replace(ComputedSource + ".", "")} and reads {what}, which is not a column of {entity.Type.Name}: a computed member is read from the entity's own columns";
                return null;
            }

            return $"({rewritten})";
        }

        /// <summary>Each <c>__e.Column</c> of a body replaced by the column's read, the column added to the select list once.</summary>
        private sealed class ColumnReads(EntityModel entity, List<string> columns) : CSharpSyntaxRewriter
        {
            private readonly Dictionary<string, int> _ordinals = new(System.StringComparer.Ordinal);

            /// <summary>The first member read from the entity that is not a column of its own; null when every one is.</summary>
            public string? Problem { get; private set; }

            public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
            {
                if (node.Expression is not IdentifierNameSyntax { Identifier.ValueText: ComputedSource })
                    return base.VisitMemberAccessExpression(node);
                var member = node.Name.Identifier.ValueText;
                if (entity.Column(member) is not { Owner: null } column)
                {
                    Problem ??= member;
                    return node;
                }

                if (!_ordinals.TryGetValue(member, out var ordinal))
                {
                    columns.Add(column.Select("t"));
                    ordinal = columns.Count - 1;
                    _ordinals[member] = ordinal;
                }

                return SyntaxFactory.ParseExpression("(" + column.Read("r", ordinal.ToString(), "d") + ")").WithTriviaFrom(node);
            }

            public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
            {
                if (node.Identifier.ValueText == ComputedSource)
                    Problem ??= "the entity itself";
                return base.VisitIdentifierName(node);
            }
        }
    }
}
