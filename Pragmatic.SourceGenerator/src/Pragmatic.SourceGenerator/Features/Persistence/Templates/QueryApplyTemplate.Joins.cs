using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     The step a query with a key join generates: a real join, and the target's columns in the result.
/// </summary>
/// <remarks>
///     <para>
///         <c>[Join&lt;T&gt;(ForeignKey = …, TargetKey = …)]</c> exists for an entity no navigation
///         reaches. <c>Include</c> cannot express that and neither can <c>Projection</c>, which is one
///         entity in and one result out — so the join owns the whole step and generates
///         <c>Aggregate</c>, whose shape is filtered set in, projected set out.
///     </para>
///     <para>
///         Every step carries an anonymous <b>carrier</b> with one member per source: <c>__r</c> for
///         the root and <c>__t0</c>, <c>__t1</c>, … for the joined targets. It costs a
///         <c>Select(__r =&gt; new { __r })</c> that EF Core collapses, and it buys the one thing a
///         hand-written chain never has: every step is spelled the same whether it is the first or the
///         fourth.
///     </para>
/// </remarks>
internal sealed partial class QueryApplyTemplate
{
    private const string JoinSourceProvider =
        "global::Pragmatic.Persistence.Query.Interfaces.IJoinSourceProvider";

    private const string Root = "__r";

    /// <summary>
    ///     The boundary whose <c>DbContext</c> the join reads from, spelled as the invoker spells it.
    /// </summary>
    /// <remarks>
    ///     ⚠️ It has to be the <b>root's</b> boundary and not the target's: EF Core composes a join
    ///     only inside one <c>DbContext</c> instance, and a host builds one per boundary. A target of
    ///     another boundary is reached by declaring <c>[ReadAccess&lt;T&gt;]</c> there, which puts it
    ///     in this model.
    /// </remarks>
    private string JoinBoundary => _model.BoundaryTypeName!.StartsWith("global::", System.StringComparison.Ordinal)
        ? _model.BoundaryTypeName!
        : $"global::{_model.BoundaryTypeName}";

    /// <summary>The field holding the set for join <paramref name="index" />.</summary>
    private static string SourceField(int index) => $"__joinSource{index}";

    /// <summary>The carrier member holding the target of join <paramref name="index" />.</summary>
    private static string Target(int index) => $"__t{index}";

    /// <summary>
    ///     The fields the executor fills, and the method it fills them through.
    /// </summary>
    private void RenderJoinSourceBinding()
    {
        var joins = _model.KeyJoins;

        for (var index = 0; index < joins.Length; index++)
        {
            AppendLine(
                $"private System.Linq.IQueryable<global::{joins[index].TargetTypeFullName}>? "
                + $"{SourceField(index)};");
        }

        AppendLine();
        XmlSummary("Receives the sets this query's declared joins read.");
        XmlParam("sources", "Supplied by the executor, before it reads Aggregate.");

        Method("BindJoinSources", () =>
        {
            AppendLine($"var __sets = sources.ForBoundary<{JoinBoundary}>();");
            for (var index = 0; index < joins.Length; index++)
            {
                AppendLine(
                    $"{SourceField(index)} = __sets.Of<global::{joins[index].TargetTypeFullName}>();");
            }
        }, "void", [new MethodParameter(JoinSourceProvider, "sources")]);
    }

    /// <summary>
    ///     The whole step: the joins, then the result built from every source.
    /// </summary>
    private void RenderJoinedAggregate()
    {
        var entityType = $"global::{_model.EntityTypeFullName}";
        var resultType = $"global::{_model.ResultTypeFullName}";
        var joins = _model.KeyJoins;

        XmlSummary("Joins the entities no navigation reaches, and projects the result from all of them.");
        AppendLine(
            $"public System.Func<IQueryable<{entityType}>, IQueryable<{resultType}>>? Aggregate => __source =>");
        IncreaseIndent();

        // One member per source from the first line, so the second join is spelled like the first.
        AppendLine($"__source.Select({Root} => new {{ {Root} }})");

        for (var index = 0; index < joins.Length; index++)
            RenderJoinStep(joins[index], index);

        RenderJoinedSelect(resultType);
        DecreaseIndent();
    }

    private void RenderJoinStep(JoinModel join, int index)
    {
        var carried = CarriedMembers(index);

        // ⚠️ Qualified: inside `(__c, __t0) => …` the carrier's members are reached through __c, and a
        // bare `__r` is a CS0103 in a file the author did not write.
        var next = $"new {{ {string.Join(", ", carried.Select(m => $"__c.{m}"))}, {Target(index)} }}";
        var source = BoundSource(index);
        var outerKey = $"__c => __c.{Root}.{join.ForeignKeyMember}";
        var innerKey = InnerKeySelector(join, index);

        switch (join.JoinType)
        {
            // Every row of the root survives: GroupJoin pairs it with a possibly empty group, and
            // DefaultIfEmpty turns that into one null row. This is the whole of Type.Left.
            case JoinTypeKind.Left:
                AppendLine($".GroupJoin({source}, {outerKey}, {innerKey}, (__c, __g{index}) => new {{ __c, __g{index} }})");
                AppendLine(
                    $".SelectMany(__p => __p.__g{index}.DefaultIfEmpty(), (__p, {Target(index)}) => new {{ "
                    + $"{string.Join(", ", carried.Select(m => $"__p.__c.{m}"))}, {Target(index)} }})");
                break;

            // No keys at all: every row of the root paired with every row of the target.
            case JoinTypeKind.Cross:
                AppendLine($".SelectMany(__c => {source}, (__c, {Target(index)}) => {next})");
                break;

            default:
                AppendLine($".Join({source}, {outerKey}, {innerKey}, (__c, {Target(index)}) => {next})");
                break;
        }
    }

    /// <summary>
    ///     The inner key, cast when the two sides are not the same type.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <c>Queryable.Join</c> takes one key type for both selectors, so a nullable foreign key
    ///     against a non-nullable target key does not compile at all. The cast goes on the target's
    ///     side because the foreign key is what the author declared the relation with.
    /// </remarks>
    private static string InnerKeySelector(JoinModel join, int index)
    {
        var read = $"{Target(index)}_k.{join.TargetKeyMember}";
        return join.ForeignKeyTypeFullName is { } fk && fk != join.TargetKeyTypeFullName
            ? $"{Target(index)}_k => ({fk}){read}"
            : $"{Target(index)}_k => {read}";
    }

    /// <summary>The carrier members present before join <paramref name="index" /> is added.</summary>
    private static List<string> CarriedMembers(int index)
    {
        var members = new List<string> { Root };
        for (var earlier = 0; earlier < index; earlier++)
            members.Add(Target(earlier));
        return members;
    }

    /// <summary>
    ///     The bound set, with the refusal that names what was not done.
    /// </summary>
    /// <remarks>
    ///     A query executed without an executor never had <c>BindJoinSources</c> called. The
    ///     alternative to this guard is a <see cref="System.NullReferenceException" /> raised from
    ///     inside a generated lambda, with nothing in the message to say which declaration it belongs
    ///     to.
    /// </remarks>
    private string BoundSource(int index)
        => $"{SourceField(index)} ?? throw new System.InvalidOperationException("
           + $"\"{_model.TypeName} declares a [Join<{_model.KeyJoins[index].TargetTypeName}>] whose source "
           + "was never bound. Execute it through IQueryExecutor, which calls BindJoinSources.\")";

    private void RenderJoinedSelect(string resultType)
    {
        AppendLine($".Select(__c => new {resultType}");
        AppendLine("{");
        IncreaseIndent();

        foreach (var property in _model.JoinedResultProperties)
        {
            var owner = property.IsFromRoot ? Root : Target(property.JoinIndex);
            var read = $"__c.{owner}.{property.SourceProperty}";

            // An outer join's target can be absent, and the read has to answer the type's default
            // rather than raise. The guard is what makes Left different from Inner in the result and
            // not only in the row count.
            AppendLine(property.NeedsNullGuard
                ? $"{property.Name} = __c.{owner} == null ? default({property.TypeFullName}) : {read},"
                : $"{property.Name} = {read},");
        }

        DecreaseIndent();
        AppendLine("});");
    }
}
