using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Repository;

/// <summary>
///     Serialises every class that mutates the static <c>LookupResolver</c>.
/// </summary>
/// <remarks>
///     ⚠️ Not a preference. The resolver is a process-wide static and these classes call
///     <c>Reset()</c> around each case, so run in parallel one class empties the other's
///     registrations mid-test — a failure that appears in whichever class happened to be reading, and
///     passes in isolation every time. Adding a class here is the price of touching that static from a
///     test.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class LookupResolverCollection
{
    public const string Name = "LookupResolver";
}
