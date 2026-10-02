using Xunit;

namespace Pragmatic.Testing.Tests;

/// <summary>
///     The classes that write to <see cref="PragmaticContractHost" />, run one at a time.
/// </summary>
/// <remarks>
///     ⚠️ <c>PragmaticContractHost</c> is <b>static</b> — deliberately, because the generated contract
///     classes have no constructor a fixture could reach. So two test classes that set its client, its
///     hooks or its per-boundary registrations are writing to the same object, and xUnit runs different
///     collections in parallel. One class alone is green and the same class beside a second one fails
///     on an exception message about a boundary the other test has just registered. A single class is
///     safe only by being the only one.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class TheProcessWideContractHostCollection
{
    public const string Name = "The process-wide contract host";
}
