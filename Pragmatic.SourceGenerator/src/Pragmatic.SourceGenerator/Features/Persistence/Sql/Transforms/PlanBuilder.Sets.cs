using Pragmatic.Persistence.Sql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     Set operations and DISTINCT. A chain with <c>Union</c>, <c>Concat</c>, <c>Except</c> or <c>Intersect</c> is rendered as a
///     wrapper over its members: <c>SELECT t.c0, t.c1… FROM (member UNION member) t ORDER BY … LIMIT …</c>. Each member is a
///     builder of its own (this one's parameters, pieces and slots continued), selecting its columns as <c>cN</c>; no member
///     orders or pages (the dialects disagree on that), the wrapper does. The steps before the first operation are the first
///     member's, the operation's other chain is the next member's, the steps after are the wrapper's (OrderBy, Skip, Take,
///     Distinct, a further operation). A <c>Distinct</c> before a Count makes the same wrapper with one member.
/// </summary>
internal sealed partial class PlanBuilder
{
    private bool _distinct;
    private List<(string Operator, PlanBuilder Member)>? _members; // the members of a set operation, in order, with the operator before each; this builder is then the wrapper
    private PlanBuilder? _current; // the member the steps go to, until the wrapper phase
    private bool _wrapperPhase;
    private bool _distinctCloses; // a Distinct on the member ends it: the wrapper counts the distinct rows
    private bool _isMember; // a nested level used as a member: rendered as a wrapper, so the operators never rely on the dialect's precedence
    private int _memberColumns;

    /// <summary>Why a step of a set operation or its wrapper was refused: reported at the operation's call site, or at the terminal (the wrapper's steps have valid call sites of their own).</summary>
    public string? StepError { get; private set; }

    /// <summary>The operators and the members, for the census.</summary>
    public string MembersSummary => _members is null ? (_distinct ? "DISTINCT" : "") : string.Join(" ", _members.Select(m => (m.Operator.Length > 0 ? m.Operator + " " : "") + (m.Member._members is not null ? "(" + m.Member.MembersSummary + ")" : "member")));

    /// <summary>A member (or a join's inner chain, on <paramref name="entity" />): the parent's parameters, pieces, constants and slots continued, its own statement.</summary>
    private PlanBuilder(PlanBuilder wrapper, EntityModel? entity = null)
    {
        _entity = entity ?? wrapper._entity;
        _entities = wrapper._entities;
        _compilation = wrapper._compilation;
        _valuesInSlots = wrapper._valuesInSlots;
        _kinds = wrapper._kinds;
        _binds = wrapper._binds;
        _fields = wrapper._fields;
        _constants = wrapper._constants;
        _dialectPieces = wrapper._dialectPieces;
        ValueSlots = wrapper.ValueSlots;
        _slot = wrapper._slot;
        _all = wrapper._all;
        _all.Add(this);
    }

    /// <summary>
    ///     Decides, before the steps are added, whether this builder wraps members: when a step is a set operation, or a Distinct
    ///     feeds a Count (the dialects count the distinct rows only through a derived table).
    /// </summary>
    public void Prepare(IReadOnlyList<SqlInterceptorGenerator.Step> steps, string terminal)
    {
        var setOperation = steps.Any(s => SqlInterceptorGenerator.IsSetOperation(s.Name));
        _distinctCloses = !setOperation && terminal == "CountAsync" && steps.Any(s => s.Name == "Distinct");
        if (!setOperation && !_distinctCloses)
            return;
        _members = [];
        _current = new PlanBuilder(this);
    }

    /// <summary>A step of a chain with members: forwarded to the current member, or applied to the wrapper. Null when the step is none of this builder's business.</summary>
    private bool? SetStep(SqlInterceptorGenerator.Step step, SemanticModel model)
    {
        if (_members is null)
            return step.Name == "Distinct" ? Distinct() : null;
        if (SqlInterceptorGenerator.IsSetOperation(step.Name))
        {
            if (step.Inner is null)
                return Refuse("the other chain of the set operation could not be read");
            if (_wrapperPhase)
                Nest(); // a further operation: what is there becomes the first member of a new level, so the dialects' precedences never apply
            else if (!CloseMember())
                return false;
            var member = new PlanBuilder(this);
            member.Prepare(step.Inner, "");
            foreach (var inner in step.Inner)
            {
                if (member.AddStep(inner, model))
                    continue;
                StepError = member.StepError;
                return false;
            }

            _members!.Add((Operator(step.Name), member));
            _slot = member._slot;
            if (!CloseMember(member))
                return false;
            _wrapperPhase = true;
            return true;
        }

        if (!_wrapperPhase)
        {
            var ok = _current!.AddStep(step, model);
            if (!ok)
                StepError = _current.StepError;
            else if (step.Name == "Distinct" && _distinctCloses)
            {
                if (!CloseMember())
                    return false;
                _wrapperPhase = true;
            }

            return ok;
        }

        return WrapperStep(step, model);
    }

    private bool Distinct()
    {
        if (_groupKey is not null)
            return Refuse("Distinct does not apply to groups");
        _distinct = true;
        return true;
    }

    private bool Refuse(string why)
    {
        StepError = why;
        return false;
    }

    private static string Operator(string name) => name switch
    {
        "Union" => "UNION",
        "Concat" => "UNION ALL",
        "Except" => "EXCEPT",
        _ => "INTERSECT",
    };

    /// <summary>The first member closed: the steps so far, validated as a member; the wrapper takes its projection (the reader's) and its slot count.</summary>
    private bool CloseMember()
    {
        var member = _current!;
        _members!.Add(("", member));
        _slot = member._slot;
        return CloseMember(member);
    }

    private bool CloseMember(PlanBuilder member)
    {
        if (member._orderings.Count > 0 || member._hasSkip || member._hasTake || member._cursorParameters is not null)
            return Refuse("a member of a set operation has no ordering, paging or cursor; order and page the result, after the operation");
        if (member._includes.Count > 0 || member._projection is { Collections.Count: > 0 })
            return Refuse("a member of a set operation loads no collection: Include and a DTO with a collection work on one table's rows");
        if (member._groupKey is not null)
            return Refuse("a grouped chain is not a member of a set operation (ASSUMPTION of the spike: group the result of the operation in a later step)");
        if (member._raw is not null)
            return Refuse("a hand-written statement is not a member of a set operation: write the whole statement by hand");
        HoistMember(member);
        var first = _members![0].Member;
        if (_members.Count == 1)
        {
            _projection = member._projection;
            _memberColumns = member.MemberColumns().Count;
            return true;
        }

        if (member._projection?.Construct != first._projection?.Construct || member.MemberColumns().Count != _memberColumns)
            return Refuse("the members of a set operation project the same members in the same order (the first member's reader reads every row), or none");
        return true;
    }

    /// <summary>The wrapper becomes a member of a new level, whose other members follow.</summary>
    private void Nest()
    {
        var level = new PlanBuilder(this)
        {
            _members = _members,
            _projection = _projection,
            _memberColumns = _memberColumns,
            _distinct = _distinct,
            _wrapperPhase = true,
        };
        _distinct = false;
        _members = [("", level)];
    }

    /// <summary>After the operation: the ordering (on the entity's own columns, as the result's <c>cN</c>), the paging, a Distinct.</summary>
    private bool WrapperStep(SqlInterceptorGenerator.Step step, SemanticModel model)
    {
        switch (step.Name)
        {
            case "OrderBy":
            case "OrderByDescending":
            case "ThenBy":
            case "ThenByDescending":
            {
                if (step.Lambda is null || !SqlInterceptorGenerator.TryParameter(model, step.Lambda, out var parameter))
                    return false;
                var translator = new LambdaTranslator(model, _entities, _entity, parameter);
                var column = translator.ColumnOf(SqlInterceptorGenerator.Body(step.Lambda));
                if (column is null || translator.Joins.Count > 0 || translator.ParameterPaths.Count > 0 || translator.DialectPieces.Count > 0 || WrapperColumn(column) is not { } result)
                    return Refuse("after a set operation the ordering is on the columns the members select (o => o.Column); order on a navigation or a function in the members' projection");
                var reset = !step.Name.StartsWith("Then", StringComparison.Ordinal);
                if (reset)
                    _orderings.Clear();
                _orderings.Add(result + (step.Name.EndsWith("Descending", StringComparison.Ordinal) ? " DESC" : ""));
                return true;
            }

            case "Skip":
            case "Take":
            case "AsNoTracking":
                return ChainStep(step, model);
            case "Distinct":
                return Distinct();
            default:
                return Refuse($"after a set operation only OrderBy/ThenBy, Skip, Take, Distinct, another operation and the terminals apply; '{step.Name}' goes in the members");
        }
    }

    /// <summary>An entity column of the root (<c>t."X"</c>) as the result's column (<c>t.cN</c>); null when the members do not select it as such.</summary>
    private string? WrapperColumn(string column)
    {
        var columns = _members![0].Member.MemberColumns();
        var index = columns.IndexOf(column);
        return index < 0 ? null : "t.c" + index;
    }

    /// <summary>The columns a member selects, without aliases: the entity's, a projection's, or a nested level's result columns.</summary>
    private List<string> MemberColumns()
    {
        if (_members is not null)
            return Enumerable.Range(0, _memberColumns).Select(i => "t.c" + i).ToList();
        return _projection?.Columns.ToList() ?? _entity.Columns.Select(c => c.Select(_alias)).ToList();
    }

    /// <summary>
    ///     True when the set operation renders without a wrapper: the members aliased their columns <c>cN</c>, so an ORDER BY on them
    ///     and the paging apply to the compound itself (every dialect takes result column names there). Not for a DISTINCT or a
    ///     scalar terminal on the result, and not on SQL Server when a page has no ordering (its constant ORDER BY is no result column).
    /// </summary>
    private bool IsFlatCompound(SqlDialect dialect)
        => !_isMember && !_distinct && !IsScalar && !(dialect == SqlDialect.SqlServer && _orderings.Count == 0 && (_skipParameter is not null || _limit is not null));

    private string Compound(SqlDialect dialect)
    {
        var sql = new StringBuilder();
        foreach (var (op, member) in _members!)
        {
            if (op.Length > 0)
                sql.Append(' ').Append(op).Append(' ');
            sql.Append(member.Statement(dialect, member._selectList));
        }

        if (_orderings.Count > 0)
            sql.Append(" ORDER BY ").Append(string.Join(", ", _orderings.Select(o => o.Substring("t.".Length)))); // t.cN → cN: the compound's result columns
        sql.Append(SqlDialects.Paging(dialect, _skipParameter, _limit));
        return ForDialect(Resolve(sql.ToString(), dialect), dialect);
    }

    /// <summary>The members settled for the terminal: each selects its columns as <c>cN</c>; the wrapper selects them back, or counts them.</summary>
    private void FinishMembers(string terminal)
    {
        foreach (var (_, member) in _members!)
        {
            member._terminal = "ToListAsync";
            member._isMember = true;
            if (member._members is not null)
                member.FinishMembers("ToListAsync");
            member._selectList = string.Join(", ", member.MemberColumns().Select((c, i) => $"{c} AS c{i}"));
        }

        _selectList = terminal switch
        {
            "CountAsync" => "COUNT(*)",
            "AnyAsync" => "1",
            _ => string.Join(", ", Enumerable.Range(0, _memberColumns).Select(i => "t.c" + i)),
        };
    }

    /// <summary>The wrapper's FROM: the members' statements joined by their operators, as a derived table.</summary>
    private void AppendMembers(StringBuilder sql, SqlDialect dialect)
    {
        sql.Append(" FROM (");
        foreach (var (op, member) in _members!)
        {
            if (op.Length > 0)
                sql.Append(' ').Append(op).Append(' ');
            sql.Append(member.Statement(dialect, member._selectList));
        }

        sql.Append(") t");
    }

    /// <summary>What a set operation or a Distinct refuses at the terminal; null when the combination holds.</summary>
    private string? MembersProblem(string terminal)
    {
        if (_members is null)
            return _distinct && IsAggregate ? "Distinct does not apply to the scalar aggregates (SUM, MIN, MAX, AVG read the rows)" : null;
        if (!_wrapperPhase)
            return "the set operation's members could not be closed";
        if (IsAggregate || IsCommand)
            return "after a set operation the terminals read rows, count them or test them: the scalar aggregates and the commands work on one table";
        if (_includes.Count > 0)
            return "Include goes in the members' chains, not after the operation";
        return null;
    }

    /// <summary>The members' unindexed predicate columns too, for the index analysis.</summary>
    private IEnumerable<ColumnModel> MemberPredicateColumns()
        => _members is null ? [] : _members.SelectMany(m => m.Member._predicateColumns.Concat(m.Member.MemberPredicateColumns()));
}
