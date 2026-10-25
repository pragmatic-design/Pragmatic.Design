using Xunit;

namespace Pragmatic.Result.Tests.Unit;

/// <summary>
///     The classes that read or reset the process-wide <c>ErrorTypeRegistry</c>. Membership serializes them: a <c>Clear()</c> in one
///     class, run in parallel with a round-trip in another, left the reader an empty registry for that moment, and an error that should
///     come back as its type came back as a <c>SerializedError</c>.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ErrorTypeRegistryCollection
{
    public const string Name = "ErrorTypeRegistry";
}
