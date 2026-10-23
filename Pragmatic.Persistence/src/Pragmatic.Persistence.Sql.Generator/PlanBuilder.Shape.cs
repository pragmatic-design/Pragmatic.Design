using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     A <c>ShapeAsync</c> plan: its select list aliased with the row type's member names, so the runtime can wrap the statement as a
///     derived table and let the client filter, sort and select by member; the members and their kinds (the whitelist) emitted with the plan.
/// </summary>
internal sealed partial class PlanBuilder
{
    /// <summary>The members a ShapeAsync plan exposes, with the kind of each; null on the other plans.</summary>
    public List<(string Name, string Kind)>? ShapeColumns { get; private set; }

    private bool ShapeSelectList(Location where, List<Diagnostic> diagnostics)
    {
        string? problem = null;
        if (_includes.Count > 0)
            problem = "a shaped read works on one statement: project the rows (a DTO with the members the client sees) instead of an Include";
        else if (_groupKey is not null || _distinct || _members is not null || _rowOpen)
            problem = "a shaped read works on the entity's rows or on a projection of them: not on groups, DISTINCT, set operations or an open joined row";
        else if (_projection is { Collections.Count: > 0 })
            problem = "a shaped read works on one statement: a DTO with collections has companion statements; shape a DTO without them";
        else if (_projection is not null && _projection.Members is null)
            problem = "the projection has no member names to expose: project into a DTO or an anonymous object with named members, one column each";
        if (problem is not null)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, "ShapeAsync", problem));
            return false;
        }

        List<(string Name, string Kind)> members;
        List<string> expressions;
        if (_projection is null)
        {
            members = _entity.Columns.Where(c => !c.IsDiscriminator).Select(c => (c.Property.Replace(".", ""), LambdaTranslator.KindOf(c.Store))).ToList();
            expressions = _entity.Columns.Where(c => !c.IsDiscriminator).Select(c => c.Select("t")).ToList();
            if (expressions.Count != _entity.Columns.Count)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, "ShapeAsync", "a hierarchy's rows carry a discriminator the client does not see: project the rows into a DTO"));
                return false;
            }
        }
        else
        {
            members = _projection.Members!;
            expressions = _projection.Columns;
        }

        if (members.Select(m => m.Name).Distinct(System.StringComparer.OrdinalIgnoreCase).Count() != members.Count)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, "ShapeAsync", "two members of the rows share a name (case aside): the client could not tell them apart"));
            return false;
        }

        ShapeColumns = members;
        _selectList = string.Join(", ", expressions.Select((e, i) => $"{e} AS \"{members[i].Name}\""));
        return true;
    }

    /// <summary>The C# of the plan's shape columns, for the plan's constructor; null without ShapeAsync.</summary>
    public string? ShapeColumnsLiteral => ShapeColumns is null ? null : "new global::Pragmatic.Persistence.Sql.SqlShapeColumn[] { " + string.Join(", ", ShapeColumns.Select(c => $"new(\"{c.Name}\", global::Pragmatic.Persistence.Sql.SqlParamKind.{c.Kind})")) + " }";
}
