namespace Pragmatic.Authoring;

/// <summary>
///     Assembly-level metadata, emitted by the source generator, listing an endpoint whose operation body is
///     still <c>throw Behavior.Pending()</c> — i.e. not yet implemented. Tooling that runs against the compiled
///     assembly (notably the contract-test generator) reads these to skip not-yet-implemented endpoints: a
///     pending endpoint gets no contract test, and the test appears automatically once the body is implemented
///     and the metadata is no longer emitted. Inert at runtime.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class PendingContractAttribute(string typeName) : Attribute
{
    /// <summary>The fully-qualified name of the endpoint type whose behavior is pending implementation.</summary>
    public string TypeName { get; } = typeName;
}
