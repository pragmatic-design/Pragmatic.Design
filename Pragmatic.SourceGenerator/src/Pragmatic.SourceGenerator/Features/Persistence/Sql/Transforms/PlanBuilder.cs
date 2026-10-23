using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     Turns one chain (its operators in order, then the terminal) into the text of a <c>SqlPlan</c> class:
///     the SQL for each dialect, the parameters' kinds, the bind that fills them, the reader, the loader of
///     the collection includes. Values reach the bind as constants (fields of the plan), as members of the
///     argument stored in the chain's slot (the two-parameter form), or as the value itself stored in the
///     slot (a [Query] definition: its parameters are the values). A collection include is a companion
///     statement in the same command (a child builder sharing this one's parameters), whose WHERE selects the
///     children of the parent statement's rows through a subquery on the parent's keys: the loader reads the
///     result sets that follow the rows' own, so the whole tree costs one round trip.
/// </summary>
internal sealed partial class PlanBuilder
{
    private static readonly Regex Placeholder = new(@"\{(\d+)\}", RegexOptions.Compiled);

    private EntityModel _entity; // an OfType step narrows it to a derived type's model
    private int _lock = -1; // a ForUpdate step: the SqlLockMode (Wait 0, SkipLocked 1, NoWait 2); -1 without one
    private readonly Func<INamedTypeSymbol, EntityModel?> _entities;
    private readonly Compilation _compilation;
    private readonly bool _valuesInSlots;
    private readonly List<string> _joins = [];
    private readonly HashSet<string> _innerAliases = new(StringComparer.Ordinal); // optional navigations the predicates need a row for: their LEFT JOIN becomes INNER
    private readonly List<string> _predicates = [];
    private readonly List<string> _having = []; // predicates on the groups: a Where after GroupBy
    private readonly List<string> _kinds; // shared with the child builders: one command, one parameter list
    private readonly List<string> _binds; // C# statements over `q`, `p`, `d`
    private readonly List<string> _fields; // static fields of the plan class: the rules' trees, the constants
    private readonly Dictionary<string, string> _constants;
    private readonly List<DialectPiece> _dialectPieces; // shared too: the {dN} tokens of every fragment of the command, resolved per dialect
    private readonly List<string> _orderings = [];
    private readonly PlanBuilder? _parent; // the statement this one selects the children of (a collection include)
    private readonly string? _parentKey; // the parent's key as the parent statement selects it (t."Id", j_Customer."Id")
    private readonly string? _foreignKey; // the child's column that holds the parent's key
    private string[]? _foreignKeys; // a composite parent's: the child's columns, one per key column; the parent statement selects them as k0, k1, …
    private readonly List<PlanBuilder> _children = []; // on the root: the companion statements, depth first
    private int? _perParentTake; // a companion: at most this many children per parent (a window over _orderings, partitioned by the foreign key)
    private string? _includeProblem; // on the root: what a companion could not be built as, reported on the terminal
    private SqlInterceptorGenerator.Projection? _projection;
    private bool _hasSkip;
    private bool _hasTake;
    private bool _withoutFilters;
    private bool _includeDeleted; // IncludeDeleted(): a [SoftDelete] entity's deleted rows too, the rules still in
    private readonly HashSet<INamedTypeSymbol> _withoutRules = new(SymbolEqualityComparer.Default); // WithoutFilter<TRule>(): the rules left out one by one
    private GroupKey? _groupKey;
    private int _slot;
    private string _terminal = "";
    private string _elementType = "";
    private string? _aggregateColumn;
    private string? _aggregateReader;
    private string? _skipParameter;
    private string? _takeParameter;
    private string? _limit;
    private string _selectList = "";
    private string? _asOfParameter; // AsOf: the @pN of the instant, FOR SYSTEM_TIME AS OF on SQL Server
    private string? _raw; // a hand-written statement as the root (session.Sql<T>): the FROM, its holes the first parameters
    private string? _rawFrom; // a table-valued function as the root (session.Function<T>): the FROM source itself, its arguments the first parameters
    private PlanBuilder? _cte; // a common table expression as the root (session.With): the anchor's statement, the FROM is the named query
    private string? _cteStep; // the recursive step's ON condition (t against tree); null for a plain CTE
    private bool _cteMaterialized;
    private string _alias = "t"; // the root row's alias: `t`, or a join's `jN` for the inner chain of an explicit join
    private RowShape? _row; // what the chain's lambda parameter stands for: the entity under its alias, or a join's composite row
    private bool _rowOpen; // after a join: the row is open to further operators (a Select closes it)

    /// <summary>The shape the chain's lambda parameter stands for at this point of the chain.</summary>
    internal RowShape Row => _row ??= new EntityRow(_entity, _alias);

    public PlanBuilder(EntityModel entity, Func<INamedTypeSymbol, EntityModel?> entities, Compilation compilation, bool valuesInSlots)
    {
        _entity = entity;
        _entities = entities;
        _compilation = compilation;
        _valuesInSlots = valuesInSlots;
        _kinds = [];
        _binds = [];
        _fields = [];
        _constants = new Dictionary<string, string>(StringComparer.Ordinal);
        _dialectPieces = [];
        _all = [this];
    }

    /// <summary>
    ///     A companion statement: the rows of <paramref name="child" /> whose <paramref name="foreignKey" /> is among the keys <paramref name="parent" />'s
    ///     statement selects as <paramref name="parentKey" />. Through a join table (<paramref name="linkJoin" />) the foreign key is the link's column,
    ///     under the link's alias <c>t_l</c>, and comes back as the statement's last column (<see cref="_linkKeyReader" />) for the loader to group on.
    /// </summary>
    private PlanBuilder(EntityModel child, PlanBuilder parent, string parentKey, string foreignKey, string? linkJoin = null)
    {
        _entity = child;
        _entities = parent._entities;
        _compilation = parent._compilation;
        _valuesInSlots = false;
        _kinds = parent._kinds;
        _binds = parent._binds;
        _fields = parent._fields;
        _constants = parent._constants;
        _dialectPieces = parent._dialectPieces;
        _parent = parent;
        _all = parent._all;
        _all.Add(this);
        _parentKey = parentKey;
        _foreignKey = foreignKey;
        _foreignKeyAlias = linkJoin is null ? "t" : "t_l";
        if (linkJoin is not null)
            _joins.Add(linkJoin);
        _terminal = "ToListAsync";
        _elementType = child.FullName;
    }

    private readonly string _foreignKeyAlias = "t"; // the alias the companion's foreign key is read under: the child's, or the join table's
    private string? _linkKeyReader; // a companion through a join table: the C# reading the parent's key from the row's last column

    private PlanBuilder Root
    {
        get
        {
            var builder = this;
            while (builder._parent is not null)
                builder = builder._parent;
            return builder;
        }
    }

    /// <summary>In the values mode: the C# text to store in each slot, by slot index (the [Query] body writes <c>WithSlot(j, text)</c>).</summary>
    public List<string> ValueSlots { get; } = [];

    /// <summary>The argument text of each Skip and Take step, for a [Query] body to repeat.</summary>
    public List<string> SkipTexts { get; } = [];

    public List<string> TakeTexts { get; } = [];

    /// <summary>How many slots the chain uses (lambdas or values).</summary>
    public int Slots => _valuesInSlots ? ValueSlots.Count : _slot;

    public bool IsAnonymous => _projection?.IsAnonymous == true;

    public bool IsAggregate => _terminal is "SumAsync" or "MinAsync" or "MaxAsync" or "AverageAsync";

    public bool IsScalar => _terminal is "CountAsync" or "AnyAsync" || IsAggregate || IsCommand;

    /// <summary>The row type the plan reads: the entity, the projection (<c>TElement</c> when anonymous), or the aggregate's value.</summary>
    public string ElementType => _elementType;

    /// <summary>The chain's element at the terminal: the entity, or the projection.</summary>
    public string ChainElement => IsAnonymous ? "TElement" : _projection?.ResultName ?? _entity.FullName;

    public string PlanType(string className) => IsAnonymous ? className + "<TElement>" : className;

    /// <summary>The <c>?</c> of an or-default terminal's result: on a reference row type; a value type (a scalar, a record struct) comes back as it is, as <c>T?</c> on the unconstrained T of the declaration.</summary>
    private string OrDefault => _projection?.IsValueType == true ? "" : "?";

    /// <summary>The <c>SqlPlan</c> method the terminal runs, and what it returns (a ValueTask: the plan's terminals pool their state machines, no Task per call).</summary>
    public (string ReturnType, string Execute) Terminal => _terminal switch
    {
        "ToListAsync" => ($"global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.List<{_elementType}>>", "ToListAsync"),
        "ToArrayAsync" => ($"global::System.Threading.Tasks.ValueTask<{_elementType}[]>", "ToArrayAsync"),
        "ShapeAsync" => ($"global::System.Threading.Tasks.ValueTask<global::Pragmatic.Persistence.Sql.SqlShapeResult<{_elementType}>>", "ShapeAsync"),
        "AsAsyncEnumerable" => ($"global::System.Collections.Generic.IAsyncEnumerable<{_elementType}>", "Enumerate"),
        "FirstOrDefaultAsync" => ($"global::System.Threading.Tasks.ValueTask<{_elementType}{OrDefault}>", "FirstOrDefaultAsync"),
        "FirstAsync" => ($"global::System.Threading.Tasks.ValueTask<{_elementType}>", "FirstAsync"),
        "SingleAsync" => ($"global::System.Threading.Tasks.ValueTask<{_elementType}>", "SingleAsync"),
        "SingleOrDefaultAsync" => ($"global::System.Threading.Tasks.ValueTask<{_elementType}{OrDefault}>", "SingleOrDefaultAsync"),
        "CountAsync" => ("global::System.Threading.Tasks.ValueTask<int>", "CountAsync"),
        "AnyAsync" => ("global::System.Threading.Tasks.ValueTask<bool>", "AnyAsync"),
        "SumAsync" or "MinAsync" or "MaxAsync" or "AverageAsync" => ($"global::System.Threading.Tasks.ValueTask<{_elementType}>", "ScalarAsync"),
        "ToDictionaryAsync" => ("", "ToDictionaryAsync"),
        "ExecuteUpdateAsync" or "ExecuteDeleteAsync" when _returning is not null => ($"global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.List<{_elementType}>>", "ToListAsync"),
        "ExecuteUpdateAsync" or "ExecuteDeleteAsync" or "ExecuteInsertAsync" => ("global::System.Threading.Tasks.ValueTask<int>", "ExecuteAsync"),
        "SyncAsync" => ("global::System.Threading.Tasks.ValueTask<int>", "SyncAsync"),
        "ToSql" => ("string", "Sql"),
        _ => ("global::System.Data.Common.DbCommand", "ToCommand"),
    };

    /// <summary>The predicate a terminal takes (<c>FirstOrDefaultAsync(o => …)</c>).</summary>
    public bool AddPredicate(SemanticModel model, LambdaExpressionSyntax lambda)
    {
        if (_members is null)
            return Predicate(model, lambda, null, fromTerminal: true);
        LastError = "after a set operation or a Distinct that is counted, the terminal takes no predicate: filter the members with Where";
        LastErrorExpression = lambda.ToString();
        return false;
    }

    /// <summary>The terminal: validates the combination, settles the parameters, the statement and the reader. False with a diagnostic.</summary>
    private bool TryFinishPlan(string terminal, IMethodSymbol method, SeparatedSyntaxList<ArgumentSyntax> arguments, SemanticModel model, Location where, List<Diagnostic> diagnostics)
    {
        _terminal = terminal;
        if (Slots > SqlQueryLimits.MaxSlots)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, terminal, $"a chain holds at most {SqlQueryLimits.MaxSlots} predicates or captured values; merge them"));
            return false;
        }

        if (_projection is not null && (_includes.Count > 0 || (IsAggregate && !_rowOpen)))
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, terminal, "Include and the aggregates work on the entity (or a join's row), not after a projection"));
            return false;
        }

        if (_cte is not null && (_includes.Count > 0 || HasLoads))
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, terminal, "a chain rooted in With takes no collection Include: its companion statement would nest the WITH clause, which SQL Server does not allow (ASSUMPTION of the spike: read the children in a second chain)"));
            return false;
        }

        if (_lock >= 0 && (IsScalar || _groupKey is not null || _distinct || _members is not null || _raw is not null))
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, terminal, "ForUpdate locks the rows a statement returns: not an aggregate, a group, a DISTINCT, a set operation or a hand-written root"));
            return false;
        }

        if (_groupKey is not null && (_projection is null || IsScalar || _includes.Count > 0))
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, terminal, "a grouped chain ends in a Select of g.Key and the aggregates, then a list terminal; Include, Count and the scalar aggregates do not apply to groups"));
            return false;
        }

        if (terminal == "AsAsyncEnumerable" && HasLoads)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, terminal, "AsAsyncEnumerable streams the rows one at a time: a collection Include needs them together; use ToListAsync"));
            return false;
        }

        if (_row is not null && CompositeRow.HasGroup(_row))
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, terminal, "a GroupJoin is followed by a SelectMany over its group (x => x.Group.DefaultIfEmpty()) before the chain ends"));
            return false;
        }

        if (MembersProblem(terminal) is { } membersProblem)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, terminal, membersProblem));
            return false;
        }

        _elementType = ChainElement;
        if (IsCommand)
        {
            if (CommandProblem() is { } problem)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, terminal, problem));
                return false;
            }

            if (_returning is not null)
                _elementType = _returning.ResultName; // the rows read back: the entity, or the DTO
            return true; // the plan stays on the entity's chain; its ExecuteAsync answers the rows affected
        }

        if (IsAggregate)
        {
            if (arguments.Count < 1 || arguments[0].Expression is not LambdaExpressionSyntax selector || !SqlInterceptorGenerator.TryParameter(model, selector, out var parameter))
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedExpression, where, arguments.Count > 0 ? arguments[0].ToString() : terminal, "aggregate", "the aggregate takes a lambda selecting a column"));
                return false;
            }

            var translator = new LambdaTranslator(model, _entities, Row, parameter);
            _aggregateColumn = translator.ColumnOf(SqlInterceptorGenerator.Body(selector));
            var valueType = method.TypeArguments.Length == 1 ? method.TypeArguments[0] : null;
            var valueReader = valueType is null ? null : EntityModel.ReaderFor(valueType);
            if (_aggregateColumn is null || translator.ParameterPaths.Count > 0 || valueType is null || (valueReader is null && terminal != "AverageAsync"))
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedExpression, where, translator.ErrorExpression ?? selector.ToString(), "aggregate", translator.Error ?? "the aggregate's value must be a numeric column"));
                return false;
            }

            Merge(_joins, translator.Joins);
            if (terminal == "AverageAsync" && EntityModel.IsInteger(valueType))
                _aggregateColumn += " * 1E0";
            _elementType = terminal == "AverageAsync" ? "double" : valueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            _aggregateReader = terminal == "AverageAsync" ? "global::Pragmatic.Persistence.Sql.SqlValue.Average(r, 0, d)" : valueReader!.Replace("{r}", "r").Replace("{i}", "0").Replace("{d}", "d");
        }

        // Paging parameters come after the captured values, and only where the statement uses them: a count has no
        // paging, the single-row terminals limit the rows themselves (a Take before them is redundant: ASSUMPTION, not merged).
        var fixedLimit = terminal switch
        {
            "FirstOrDefaultAsync" or "FirstAsync" or "AnyAsync" => "1",
            "SingleAsync" or "SingleOrDefaultAsync" => "2",
            _ => null,
        };
        var paged = !(terminal == "CountAsync" || IsAggregate);
        if (_hasSkip && paged)
        {
            _skipParameter = "@p" + _kinds.Count;
            _binds.Add($"p[{_kinds.Count}].Value = q.SkipCount;");
            _kinds.Add("Int32");
        }

        if (_hasTake && paged && fixedLimit is null)
        {
            _takeParameter = "@p" + _kinds.Count;
            _binds.Add($"p[{_kinds.Count}].Value = q.TakeCount;");
            _kinds.Add("Int32");
        }

        _limit = fixedLimit ?? _takeParameter;
        if (_members is not null)
        {
            FinishMembers(terminal); // the wrapper reads the members' columns back, or counts them; no includes, no companions
            return true;
        }

        // The entity's columns, then those of every to-one include (depth first), whose joins come after the predicates' ones.
        var columns = _entity.Columns.Select(c => c.Select("t")).ToList();
        IncludeJoins(_includes, _entity, "t", columns);
        _selectList = terminal switch
        {
            "CountAsync" => "COUNT(*)",
            "AnyAsync" => "1",
            "SumAsync" => $"SUM({_aggregateColumn})",
            "MinAsync" => $"MIN({_aggregateColumn})",
            "MaxAsync" => $"MAX({_aggregateColumn})",
            "AverageAsync" => $"AVG({_aggregateColumn})",
            _ => _projection?.ColumnList ?? string.Join(", ", columns),
        };
        if (terminal == "ShapeAsync" && !ShapeSelectList(where, diagnostics))
            return false;
        // The collection includes, or a declared DTO's collections: one companion statement each, their parameters after this statement's (the paging included).
        if (!IsScalar)
        {
            BuildChildren(_includes, this, "t", _entity);
            if (_includeProblem is { } problem)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, "Include", problem));
                return false;
            }

            if (_projection is { Collections.Count: > 0 })
                BuildProjectedChildren(_projection, this, _entity);
        }

        return true;
    }
}

/// <summary>Limits shared with the runtime's <c>SqlQuery</c>.</summary>
internal static class SqlQueryLimits
{
    public const int MaxSlots = 8;
}
