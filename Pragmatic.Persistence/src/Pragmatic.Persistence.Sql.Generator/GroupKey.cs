using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     The key of a GroupBy: one column (<c>o => o.CustomerId</c>), or several as an anonymous object or a tuple
///     (<c>o => new { o.CustomerId, o.Status }</c>, <c>o => (o.CustomerId, o.Status)</c>). After the GroupBy, <c>g.Key</c> is the
///     scalar column, <c>g.Key.Member</c> (or <c>g.Key.Item1</c>) one column of the composite key; the GROUP BY lists them all.
/// </summary>
internal sealed class GroupKey
{
    private GroupKey(List<Part> parts, ITypeSymbol type, bool isTuple)
    {
        Parts = parts;
        Type = type;
        IsTuple = isTuple;
    }

    /// <summary>One column of the key: its member name (empty for a scalar key), its SQL and its type.</summary>
    public sealed class Part
    {
        public Part(string member, string column, ITypeSymbol type)
        {
            Member = member;
            Column = column;
            Type = type;
        }

        public string Member { get; }

        public string Column { get; }

        public ITypeSymbol Type { get; }
    }

    public List<Part> Parts { get; }

    /// <summary>The key's type as the GroupBy declares it: a column's, an anonymous type or a tuple.</summary>
    public ITypeSymbol Type { get; }

    public bool IsTuple { get; }

    public bool IsComposite => Parts.Count > 1 || Parts[0].Member.Length > 0;

    /// <summary>The GROUP BY list.</summary>
    public string GroupBy => string.Join(", ", Parts.Select(p => p.Column));

    /// <summary>The column of <c>g.Key</c> (<paramref name="member" /> null: the scalar key only) or of <c>g.Key.Member</c>; null when there is none.</summary>
    public string? Column(string? member)
    {
        if (member is null)
            return IsComposite ? null : Parts[0].Column;
        for (var i = 0; i < Parts.Count; i++)
        {
            if (Parts[i].Member == member || (IsTuple && member == "Item" + (i + 1)))
                return Parts[i].Column;
        }

        return null;
    }

    /// <summary>The C# that rebuilds a composite key from its columns read at <paramref name="ordinal" /> on: the anonymous object or the tuple.</summary>
    public string? Construct(Func<ITypeSymbol, string?> readerFor, int ordinal)
    {
        var items = new List<string>(Parts.Count);
        for (var i = 0; i < Parts.Count; i++)
        {
            var reader = readerFor(Parts[i].Type);
            if (reader is null)
                return null;
            var read = reader.Replace("{r}", "r").Replace("{i}", (ordinal + i).ToString()).Replace("{d}", "d");
            items.Add(IsTuple ? read : $"{Parts[i].Member} = {read}");
        }

        return IsTuple ? $"({string.Join(", ", items)})" : $"new {{ {string.Join(", ", items)} }}";
    }

    /// <summary>Reads the GroupBy lambda; null with <paramref name="error" /> when a part is not a column of the entity (through its navigations) or the shape is another.</summary>
    public static GroupKey? TryRead(SemanticModel model, LambdaExpressionSyntax lambda, IMethodSymbol method, EntityModel entity, Func<INamedTypeSymbol, EntityModel?> entities, List<string> joins, out string? error)
    {
        error = null;
        if (!SqlInterceptorGenerator.TryParameter(model, lambda, out var parameter) || method.TypeArguments.Length != 1)
        {
            error = "a GroupBy takes one lambda over the row";
            return null;
        }

        return TryRead(model, lambda, method.TypeArguments[0], new LambdaTranslator(model, entities, entity, parameter), joins, out error);
    }

    /// <summary>A key of the given type read with the given translator (a join's key on either side, under its alias).</summary>
    public static GroupKey? TryRead(SemanticModel model, LambdaExpressionSyntax lambda, ITypeSymbol keyType, LambdaTranslator translator, List<string> joins, out string? error)
    {
        error = null;
        var body = SqlInterceptorGenerator.Body(lambda);
        var parts = new List<Part>();
        var isTuple = false;
        string? partError = null;
        switch (body)
        {
            case AnonymousObjectCreationExpressionSyntax anonymous:
                foreach (var initializer in anonymous.Initializers)
                {
                    var member = initializer.NameEquals?.Name.Identifier.ValueText ?? (initializer.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.ValueText;
                    if (member is null || !TryPart(initializer.Expression, member))
                    {
                        error = partError ?? "an anonymous member of the key needs a name";
                        return null;
                    }
                }

                break;
            case TupleExpressionSyntax tuple:
                isTuple = true;
                for (var i = 0; i < tuple.Arguments.Count; i++)
                {
                    var argument = tuple.Arguments[i];
                    var member = argument.NameColon?.Name.Identifier.ValueText ?? (argument.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.ValueText ?? "Item" + (i + 1);
                    if (!TryPart(argument.Expression, member))
                    {
                        error = partError;
                        return null;
                    }
                }

                break;
            default:
                if (!TryPart(body, ""))
                {
                    error = partError;
                    return null;
                }

                break;
        }

        if (translator.ParameterPaths.Count > 0)
        {
            error = "a GROUP BY key is made of columns (o.Column, o.Nav.Column), without values";
            return null;
        }

        PlanBuilder.Merge(joins, translator.Joins);
        return new GroupKey(parts, keyType, isTuple);

        bool TryPart(ExpressionSyntax expression, string member)
        {
            var column = translator.ColumnOf(expression);
            var type = model.GetTypeInfo(expression).Type;
            if (column is null || type is null)
            {
                partError = translator.Error ?? "a GROUP BY key must be a column, or an anonymous object or a tuple of columns";
                return false;
            }

            parts.Add(new Part(member, column, type));
            return true;
        }
    }
}
