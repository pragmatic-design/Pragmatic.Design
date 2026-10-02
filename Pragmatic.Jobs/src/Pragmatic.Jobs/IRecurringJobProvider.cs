namespace Pragmatic.Jobs;

/// <summary>
///     Supplies the recurring job definitions discovered at compile time by the source generator.
///     One implementation is generated per assembly that declares <c>[RecurringJob]</c> classes.
/// </summary>
/// <remarks>
///     The scheduler resolves every registered provider at startup and hands each definition to
///     <see cref="IRecurringJobRegistrar"/>, which computes the first occurrence and persists it.
///     Definitions returned here are templates: persisted scheduling state (next/last execution,
///     enabled flag) always wins over what the generator emitted.
/// </remarks>
public interface IRecurringJobProvider
{
    /// <summary>Gets the recurring job definitions declared in the providing assembly.</summary>
    IReadOnlyList<RecurringJobDefinition> GetDefinitions();
}
