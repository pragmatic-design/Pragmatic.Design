namespace Pragmatic.MultiTenancy.Tests.Unit;

/// <summary>
///     Serializes test classes that mutate the static <c>AsyncLocal</c> ambient state behind
///     <see cref="TenantScope"/>, preventing cross-class interference under parallel execution.
/// </summary>
[CollectionDefinition(Name)]
public sealed class TenantScopeCollection
{
    public const string Name = "TenantScope ambient state";
}
