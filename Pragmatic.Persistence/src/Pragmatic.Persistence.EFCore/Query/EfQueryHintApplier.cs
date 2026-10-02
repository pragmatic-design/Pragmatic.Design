using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.Query.Builder;

namespace Pragmatic.Persistence.EFCore.Query;

/// <summary>
///     Carries out a <see cref="QueryBuilder{TEntity}" />'s hints, here where EF Core is.
/// </summary>
/// <remarks>
///     <para>
///         The fluent <c>AsNoTracking()</c> and <c>AsSplitQuery()</c> set a flag on the builder; this
///         turns the flag into the EF Core call. Declared where there is no EF, implemented where
///         there is — the same shape as <c>INavigationLoader</c> and <c>EfMutationHelpers</c>.
///     </para>
///     <para>
///         Installed by <c>AddPragmaticPersistenceEFCore</c>, which an application that uses EF Core
///         already calls. ⚠️ Not by a <c>[ModuleInitializer]</c>: <c>CA2255</c> refuses one in a
///         library, and it is right to — a module initializer runs when the assembly happens to be
///         loaded, which is not a guarantee anyone should build on.
///     </para>
///     <para>
///         ⚠️ This is <b>not</b> the path a generated query takes. A <c>[Query]</c> object carries
///         <c>IQueryHints</c> and <c>EfCoreQueryExecutor</c> applies them — that mechanism was always
///         alive. This one is for the hand-written builder, which is where the promise was broken.
///     </para>
/// </remarks>
public sealed class EfQueryHintApplier : IQueryHintApplier
{
    /// <summary>Installs this applier, unless one is already there.</summary>
    public static void Install() => QueryHints.Applier ??= new EfQueryHintApplier();

    public IQueryable<TEntity> Apply<TEntity>(IQueryable<TEntity> source, bool noTracking, bool splitQuery)
        where TEntity : class
    {
        Pragmatic.Ensure.Ensure.ThrowIfNull(source);

        var query = source;

        if (noTracking)
            query = query.AsNoTracking();

        if (splitQuery)
            query = query.AsSplitQuery();

        return query;
    }
}
