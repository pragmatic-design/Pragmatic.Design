namespace Pragmatic.Persistence.Query.Interfaces;

/// <summary>
///     A query that joins an entity no navigation reaches.
/// </summary>
/// <remarks>
///     <para>
///         Implemented by the generated half of a query that declares
///         <c>[Join&lt;T&gt;(ForeignKey = …, TargetKey = …)]</c>. The executor calls
///         <see cref="BindJoinSources" /> once, after it has prepared the source and before it reads
///         <see cref="IQuery{TEntity, TResult}.Aggregate" /> — which is the member a key join
///         generates, because the target's columns have to reach the result and a
///         <c>Projection</c> is one entity in, one result out.
///     </para>
///     <para>
///         ⚠️ A query that declares a key join and is executed without an executor — by hand, against
///         a list — throws from its <c>Aggregate</c>, naming this method. The alternative was a
///         <see cref="System.NullReferenceException" /> from inside a generated lambda.
///     </para>
/// </remarks>
public interface IJoiningQuery
{
    /// <summary>Hands over the sets this query's declared joins read.</summary>
    /// <param name="sources">
    ///     The provider, supplied by the executor. The generated method asks it for its own boundary's
    ///     sets: a join has to read the target from the same <c>DbContext</c> the root came from, or
    ///     EF Core cannot compose the two.
    /// </param>
    void BindJoinSources(IJoinSourceProvider sources);
}
