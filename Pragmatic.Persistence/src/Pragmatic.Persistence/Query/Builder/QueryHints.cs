namespace Pragmatic.Persistence.Query.Builder;

/// <summary>
///     Where the provider that can carry out a builder's hints is registered.
/// </summary>
/// <remarks>
///     <para>
///         <c>Pragmatic.Persistence.EFCore</c> installs one from a <c>[ModuleInitializer]</c>, the same
///         way <c>ErrorTypeRegistry</c> is filled — so an application that uses EF Core has it without
///         wiring anything, and <c>builder.AsNoTracking().Build(query)</c> means what it says.
///     </para>
///     <para>
///         ⚠️ A hint asked for with no provider installed <b>throws</b> rather than being ignored.
///         Ignoring it would give the caller the opposite of what it asked for without a word. A loud
///         failure at the first call is the lesser harm — and it can
///         only happen where EF Core is absent, which is where the hint had no meaning to begin with.
///     </para>
/// </remarks>
public static class QueryHints
{
    /// <summary>The installed provider, or null where none is.</summary>
    public static IQueryHintApplier? Applier { get; set; }
}
