using Pragmatic.Persistence.Sql;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>The predicates of a plan: each lambda translated, its values bound from constants (fields), the argument in the slot, the [Query] values or a rule's tree.</summary>
internal sealed partial class PlanBuilder
{
    private readonly List<ColumnModel> _predicateColumns = []; // the root entity's columns the chain's predicates read, for the index analysis

    /// <summary>The columns the predicates compare that have no index (the key and <c>[Indexed]</c> columns have one), each once.</summary>
    public IEnumerable<ColumnModel> UnindexedPredicateColumns => _predicateColumns.Concat(MemberPredicateColumns()).Where(c => !c.IsIndexed && !c.IsKey).Distinct();

    /// <summary>The indexed columns the chain's predicates compare with a LIKE starting with a wildcard (PRAG3022), each once.</summary>
    public List<(string Property, string Method)> LeadingWildcards { get; } = [];

    /// <summary>
    ///     True when the rows a list terminal reads are bounded: a Take, or a predicate on the entity's key (one row). A chain without either
    ///     reads every matching row, however many (PRAG3021).
    /// </summary>
    public bool IsBounded => _hasTake || _predicateColumns.Any(c => c.IsKey || (c.Owner is null && c.Property == _entity.KeyProperty));

    /// <summary>Why the last predicate failed to translate, and on which expression: a terminal's own predicate has no call site of its own to report at.</summary>
    public string? LastError { get; private set; }

    public string? LastErrorExpression { get; private set; }

    /// <summary>The emulated catalog functions of the terminal's own lambdas (a predicate terminal, a SET): the operators report theirs at their own call sites.</summary>
    public List<(string Function, SqlDialect Dialect, string Note)> TerminalEmulations { get; } = [];

    // A predicate's values, read at bind time: a constant from a field of the plan; a member of the argument stored in the slot
    // (`arg0.Since`, the two-parameter form at a call site); the value itself stored in the slot (a [Query] definition); or a
    // node of a rule's tree, built once. After a GroupBy the predicate is on the groups (HAVING); a rule's predicate is always on the rows.
    // `row` reads the lambda's parameter as another shape than the chain's current row (a root's rule, after joins: the root's entity).
    private bool Predicate(SemanticModel model, LambdaExpressionSyntax lambda, string? tree, bool fromTerminal = false, RowShape? row = null)
    {
        if (!SqlInterceptorGenerator.TryParameters(model, lambda, out var parameter, out var arg))
            return false;
        var inline = tree is null && !_valuesInSlots;
        if (arg is not null && !inline)
            return false; // a rule and a [Query] definition write the one-parameter form: their values come from the tree or from the definition's scope
        var having = tree is null && _groupKey is not null;
        var argVariable = arg is null ? null : $"arg{_slot}";
        var translator = Bound(new LambdaTranslator(model, _entities, row ?? Row, parameter, having ? _groupKey : null, arg, argVariable, inline));
        var predicate = translator.Predicate(SqlInterceptorGenerator.Body(lambda));
        if (predicate is null)
        {
            LastError = translator.Error;
            LastErrorExpression = translator.ErrorExpression;
            return false;
        }
        var offset = _kinds.Count;
        if (arg is not null)
            _binds.Add($"var {argVariable} = ({arg.Type.ToDisplayString(SqlInterceptorGenerator.Annotated)})q.Slot({_slot})!;");
        for (var i = 0; i < translator.ParameterPaths.Count; i++)
        {
            _binds.Add($"p[{offset + i}].Value = global::Pragmatic.Persistence.Sql.SqlValue.ToDatabase(d, {Value(translator, i, tree)});");
            _kinds.Add(translator.ParameterKinds[i]);
        }

        (having ? _having : _predicates).Add(Absorb(translator, Renumber(predicate, offset, translator.ParameterKinds), offset));
        if (fromTerminal)
            TerminalEmulations.AddRange(translator.Emulations);
        Merge(_joins, translator.Joins);
        if (!having)
        {
            _innerAliases.UnionWith(translator.InnerAliases);
            if (tree is null)
            {
                _predicateColumns.AddRange(translator.RootColumns); // the chain's own predicates: a rule's columns are the entity's concern, reported once where the rule is declared
                foreach (var wildcard in translator.LeadingWildcards)
                {
                    if (!LeadingWildcards.Contains(wildcard))
                        LeadingWildcards.Add(wildcard);
                }
            }
        }
        if (arg is not null)
            _slot++;
        return true;
    }

    /// <summary>
    ///     The values a translator captured bound into the plan (a bind statement and a kind each), and the given fragments renumbered
    ///     to the plan's parameters and pieces: for text built outside <see cref="Predicate" /> (a correlated chain's derived table).
    /// </summary>
    private string[] BindValues(LambdaTranslator translator, params string[] fragments)
    {
        var offset = _kinds.Count;
        for (var i = 0; i < translator.ParameterPaths.Count; i++)
        {
            _binds.Add($"p[{offset + i}].Value = global::Pragmatic.Persistence.Sql.SqlValue.ToDatabase(d, {Value(translator, i, null)});");
            _kinds.Add(translator.ParameterKinds[i]);
        }

        var pieces = _dialectPieces.Count;
        var result = new string[fragments.Length];
        for (var f = 0; f < fragments.Length; f++)
        {
            var renumbered = Renumber(fragments[f], offset, translator.ParameterKinds);
            result[f] = f == 0 ? Absorb(translator, renumbered, offset) : PieceToken.Replace(renumbered, m => "{d" + (pieces + int.Parse(m.Groups[1].Value)) + "}");
        }

        return result;
    }

    /// <summary>The C# that yields the i-th value of a translated lambda at bind time (see <see cref="Predicate" />).</summary>
    private string Value(LambdaTranslator translator, int i, string? tree)
    {
        if (translator.ParameterIsConstant[i])
            return ConstantField(translator.ParameterTexts[i]);
        if (tree is not null)
            return "global::Pragmatic.Persistence.Expressions." + translator.ParameterPaths[i].Replace("(lambda)", $"({tree})");
        if (_valuesInSlots)
        {
            ValueSlots.Add(translator.ParameterTexts[i]);
            return $"q.Slot({ValueSlots.Count - 1})";
        }

        return translator.ParameterTexts[i]; // argN.Member
    }

    /// <summary>A constant of the chain as a static field of the plan, boxed once: binding it allocates nothing.</summary>
    private string ConstantField(string literal)
    {
        if (!_constants.TryGetValue(literal, out var name))
        {
            name = $"Const_{_constants.Count}";
            _constants[literal] = name;
            _fields.Add($"private static readonly object {name} = {literal};");
        }

        return name;
    }
}
