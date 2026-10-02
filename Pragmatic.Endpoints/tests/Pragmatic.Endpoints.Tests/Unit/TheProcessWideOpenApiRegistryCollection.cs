using Xunit;

namespace Pragmatic.Endpoints.Tests.Unit;

/// <summary>
///     The cases that write <c>PragmaticOpenApiRegistry</c>, which is a static of the process.
/// </summary>
/// <remarks>
///     ⚠️ <b>One class is enough only while there is one.</b> xUnit runs a class's cases one after the
///     other, but different classes <b>in parallel</b>:
///     <see cref="ThePublishedContractHonoursEnableOpenApiTests" /> and
///     <see cref="TwoHostsInOneProcessEachPublishTheirOwnContractTests" /> both register documents, and
///     outside one collection they overwrite each other's document between the registration and the
///     HTTP read, so a case fails reporting the other class's title — in a full run, green in
///     isolation, which is the signature.
///     <para>
///         A named collection rather than a lock or a reset, because what the cases need is
///         not to interleave at all: each one reads the document it registered, over HTTP, through a
///         host it starts.
///     </para>
/// </remarks>
[CollectionDefinition(Name)]
public sealed class TheProcessWideOpenApiRegistryCollection
{
    public const string Name = "PragmaticOpenApiRegistry";
}
