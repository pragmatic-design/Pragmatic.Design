using Xunit;

namespace Pragmatic.Persistence.Tests.Entity;

/// <summary>
///     Serialises every class that mutates the static <c>LookupResolver</c>.
/// </summary>
/// <remarks>
///     ⚠️ Observed, not theorised: with these two classes in separate collections the suite went red in
///     the gate's parallel pool and green in isolation, because <c>Reset()</c> in one empties what the
///     other has just registered. Adding a class here is the price of touching that static from a test.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class LookupResolverCollection
{
    public const string Name = "LookupResolver";
}
