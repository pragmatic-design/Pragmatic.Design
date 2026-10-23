using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     The second parameter of an explicit join's lambdas: the inner entity's row, translated by a nested translator under the
///     join's alias (<c>j1</c>), its columns mapped when the inner chain is a derived table (<c>j1.cN</c>). A LEFT JOIN marks the
///     columns it reads as optional, so the projection reads them as null.
/// </summary>
internal sealed partial class LambdaTranslator
{
    private LambdaTranslator? _joined;
    private bool _joinedOptional;

    /// <summary>True when the last column came from the joined row of a LEFT JOIN: the projection reads it as null when no row matched.</summary>
    public bool LastColumnOptional { get; private set; }

    /// <summary>The navigation joins the joined side's members needed (hanging off the join's alias); empty without a joined parameter.</summary>
    public System.Collections.Generic.IReadOnlyList<string> JoinedJoins => _joined?.Joins ?? [];

    /// <summary>The joined side's navigations a comparison needed a row for, as the joined translator saw them (it does not see this one's ORs and NOTs).</summary>
    public System.Collections.Generic.IReadOnlyCollection<string> JoinedInnerAliases => _joined?.InnerAliases ?? [];

    /// <summary>The root alias this translator renders the parameter's columns under (<c>t</c> unless told otherwise: a join's inner side under its own alias).</summary>
    public LambdaTranslator Under(string alias)
    {
        if (_row is EntityRow entity)
            _row = new EntityRow(entity.Entity, alias, entity.Optional, entity.Derived);
        return this;
    }

    /// <summary>The shape the parameter stands for.</summary>
    public RowShape Row => _row;

    /// <summary>Adds the joined row: <paramref name="parameter" /> over <paramref name="entity" /> under <paramref name="alias" />; <paramref name="derived" /> when the alias is a derived table exposing the entity's columns as <c>cN</c>.</summary>
    public void WithJoined(IParameterSymbol parameter, EntityModel entity, string alias, bool optional, bool derived)
        => WithJoined(parameter, new EntityRow(entity, alias, optional, derived), optional);

    /// <summary>Adds the joined row as a shape: an entity under its alias, or a GroupJoin's group.</summary>
    public void WithJoined(IParameterSymbol parameter, RowShape row, bool optional)
    {
        var entity = row is EntityRow e ? e.Entity : row is GroupRow g ? g.Join.Entity : null;
        var alias = row is EntityRow e2 ? e2.Alias : row is GroupRow g2 ? g2.Join.Alias : "t";
        _joined = new LambdaTranslator(this, entity ?? throw new System.ArgumentException("the joined row is an entity or a group", nameof(row)), parameter, alias) { _row = row };
        _joinedOptional = optional;
    }

    /// <summary>The collection a receiver names on this row (<c>o.Lines</c>, <c>x.Order.Lines</c>): its owner entity, the owner's alias and the collection; null when it is not one.</summary>
    public (EntityModel Owner, string Alias, CollectionModel Collection)? CollectionAt(ExpressionSyntax receiver) => CollectionOf(receiver);

    /// <summary>A translator over another entity's row that reads this lambda's rows too (a correlated chain written inside the lambda).</summary>
    public LambdaTranslator NestedOver(EntityModel entity, IParameterSymbol parameter, string alias, SemanticModel? model) => Nested(entity, parameter, alias, model, null);

    private bool IsOnJoined(MemberAccessExpressionSyntax member) => _joined is not null && _joined.IsOnParameter(member);

    private bool IsJoinedParameter(IParameterSymbol parameter) => _joined is not null && SymbolEqualityComparer.Default.Equals(parameter, _joined._parameter);

    /// <summary>A member of the joined row as a column: the nested translator's rendering, mapped; the nested translator's error becomes this one's.</summary>
    private string? JoinedColumn(MemberAccessExpressionSyntax member, bool asPredicate)
    {
        var column = _joined!.Column(member);
        LastColumn = _joined.LastColumn;
        LastColumnOptional = _joinedOptional || _joined.LastColumnOptional;
        if (column is null)
            return Fail(member, _joined._error ?? "not a column of the joined row");
        if (asPredicate && _model.GetTypeInfo(member).Type?.SpecialType == SpecialType.System_Boolean)
        {
            if (InlineConstants)
            {
                DialectPieces.Add(new DialectPiece("1", "true", "1", "true", "true"));
                return $"{column} = {{d{DialectPieces.Count - 1}}}";
            }

            Add("true", "Boolean", "true", isConstant: true);
            return $"{column} = {{{_parameterPaths.Count - 1}}}";
        }

        return column;
    }
}
